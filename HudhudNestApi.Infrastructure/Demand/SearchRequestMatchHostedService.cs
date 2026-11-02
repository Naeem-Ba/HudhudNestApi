using HudhudNestApi.Application.Demand.Matching;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Domain.Demand.Entities;
using HudhudNestApi.Domain.Demand.Enums;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HudhudNestApi.Infrastructure.Demand;

/// <summary>
/// Growth engine P2 matching sweep. Every <see cref="Interval"/>: expires requests past their end date, then
/// evaluates each active request against listings published or edited since its cursor and stores the matches.
/// The requester is told about new matches (at most <see cref="MaxRequesterAlertsPerRequest"/> per sweep); a
/// request's first evaluation of the existing market is silent, because the requester sees those matches on
/// the request page right away. Advertisers get at most one "new demand" notice per listing per day.
/// A listing that no longer fits keeps its row with score 0, so it is hidden and never announced again.
/// </summary>
public sealed class SearchRequestMatchHostedService : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    /// <summary>Listings committed while a sweep runs can carry an earlier UpdatedAt; look back this far.</summary>
    internal static readonly TimeSpan CursorOverlap = TimeSpan.FromMinutes(2);

    internal const int MaxRequestsPerSweep = 500;
    internal const int MaxListingsPerRequest = 100;
    internal const int MaxRequesterAlertsPerRequest = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SearchRequestMatchHostedService> _logger;

    public SearchRequestMatchHostedService(IServiceScopeFactory scopeFactory, ILogger<SearchRequestMatchHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Search request matching sweep failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    internal sealed record SweepResult(int Expired, int NewMatches, int RequesterAlerts, int OwnerAlerts);

    internal async Task<SweepResult> RunOnceAsync(DateTime nowUtc, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<SearchRequestMatchingOptions>>().Value;
        var result = new SweepResult(0, 0, 0, 0);

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await BackgroundJobLock.TryRunAsync(
                db.Database.GetDbConnection(),
                BackgroundJobLockKeys.SearchRequestMatch,
                nameof(SearchRequestMatchHostedService),
                _logger,
                async () => result = await SweepAsync(db, notifications, options, nowUtc, ct),
                ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return result;
    }

    private async Task<SweepResult> SweepAsync(
        AppDbContext db, INotificationService notifications, SearchRequestMatchingOptions options, DateTime nowUtc, CancellationToken ct)
    {
        // 1. Expiry.
        var due = await db.PropertySearchRequests
            .Where(r => (r.Status == SearchRequestStatus.Active || r.Status == SearchRequestStatus.Paused) && r.ExpiresAt <= nowUtc)
            .Take(MaxRequestsPerSweep)
            .ToListAsync(ct);
        foreach (var request in due)
            request.ExpireIfDue(nowUtc);
        await db.SaveChangesAsync(ct);

        // 2. Matching.
        var rates = await PropertyRepository.LoadCurrencyRatesAsync(db, ct);
        var active = await db.PropertySearchRequests
            .Where(r => r.Status == SearchRequestStatus.Active && r.ExpiresAt > nowUtc)
            .OrderBy(r => r.MatchedUpTo.HasValue)
            .ThenBy(r => r.MatchedUpTo)
            .Take(MaxRequestsPerSweep)
            .ToListAsync(ct);

        var newMatches = 0;
        var requesterAlerts = 0;
        var newDemandByListing = new Dictionary<Guid, (Guid OwnerId, int Count)>();

        foreach (var request in active)
        {
            var firstRun = request.MatchedUpTo is null;
            var since = request.MatchedUpTo;

            var candidates = db.Properties
                .AsNoTracking()
                .Where(Property.OnMarket(nowUtc))
                .Where(p => p.GovernorateId == request.GovernorateId && p.OwnerId != request.UserId)
                .Where(p => p.ListingType == request.ListingType || p.ListingType == ListingType.ForRentAndSale);
            if (request.PropertyTypeId.HasValue)
                candidates = candidates.Where(p => p.PropertyTypeId == request.PropertyTypeId);
            if (since.HasValue)
                candidates = candidates.Where(p => p.UpdatedAt > since);

            var batch = await (firstRun
                    ? candidates.OrderByDescending(p => p.UpdatedAt)
                    : candidates.OrderBy(p => p.UpdatedAt))
                .Take(MaxListingsPerRequest)
                .Select(p => new
                {
                    p.UpdatedAt,
                    Title = p.Title,
                    Facts = new ListingFacts(
                        p.Id, p.OwnerId, p.ListingType, p.GovernorateId, p.DistrictId, p.NeighborhoodId, p.PropertyTypeId,
                        p.PurchasePrice, p.WarmRent ?? p.ColdRent, p.CurrencyCode.Trim(),
                        p.Rooms, p.Area, p.HasBalcony, p.HasElevator, p.HasParkingSpace, p.FurnishingStatus),
                })
                .ToListAsync(ct);

            var ids = batch.Select(b => b.Facts.PropertyId).ToList();
            var existing = await db.SearchRequestMatches
                .Where(m => m.SearchRequestId == request.Id && ids.Contains(m.PropertyId))
                .ToDictionaryAsync(m => m.PropertyId, ct);

            var alertsThisRequest = 0;
            foreach (var item in batch)
            {
                var verdict = SearchRequestMatcher.Evaluate(request, item.Facts, rates, options);
                var fits = verdict is not null && verdict.Score >= options.MinimumScore;
                existing.TryGetValue(item.Facts.PropertyId, out var match);

                if (!fits)
                {
                    match?.Rescore(0, ["no_longer_fits"], nowUtc);
                    continue;
                }

                if (match is null)
                {
                    match = SearchRequestMatch.Create(request.Id, item.Facts.PropertyId, verdict!.Score, verdict.Reasons, nowUtc);
                    db.SearchRequestMatches.Add(match);
                    newMatches++;
                }
                else
                {
                    match.Rescore(verdict!.Score, verdict.Reasons, nowUtc);
                }

                if (match.RequesterNotifiedAt is null)
                {
                    if (firstRun)
                    {
                        match.MarkRequesterNotified(nowUtc); // shown on the request page, no push for old listings
                    }
                    else if (alertsThisRequest < MaxRequesterAlertsPerRequest)
                    {
                        match.MarkRequesterNotified(nowUtc);
                        await notifications.NotifySearchRequestMatchAsync(
                            request.UserId, item.Facts.PropertyId, item.Title, request.Id, proposed: false, ct);
                        alertsThisRequest++;
                        requesterAlerts++;
                    }
                }

                if (match.OwnerNotifiedAt is null)
                {
                    match.MarkOwnerNotified(nowUtc);
                    newDemandByListing[item.Facts.PropertyId] = newDemandByListing.TryGetValue(item.Facts.PropertyId, out var d)
                        ? (d.OwnerId, d.Count + 1)
                        : (item.Facts.OwnerId, 1);
                }
            }

            // Only move past what was really handled: a full batch leaves the rest for the next sweep.
            var handledAll = batch.Count < MaxListingsPerRequest || firstRun;
            request.MarkMatchedUpTo(handledAll ? nowUtc - CursorOverlap : batch[^1].UpdatedAt);
            await db.SaveChangesAsync(ct);
        }

        // 3. One "new demand" notice per listing per day for its advertiser.
        var ownerAlerts = 0;
        foreach (var (propertyId, demand) in newDemandByListing)
        {
            var title = await db.Properties.AsNoTracking().Where(p => p.Id == propertyId).Select(p => p.Title).FirstOrDefaultAsync(ct) ?? string.Empty;
            await notifications.NotifyListingDemandAsync(
                demand.OwnerId, propertyId, title, demand.Count, $"ldm:{propertyId:N}:{nowUtc:yyyyMMdd}", ct);
            ownerAlerts++;
        }

        if (due.Count > 0 || newMatches > 0)
        {
            _logger.LogInformation(
                "Search request sweep: {Expired} expired, {Matches} new matches, {Requester} requester and {Owner} advertiser notices.",
                due.Count, newMatches, requesterAlerts, ownerAlerts);
        }

        return new SweepResult(due.Count, newMatches, requesterAlerts, ownerAlerts);
    }
}
