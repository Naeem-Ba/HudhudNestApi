using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.Search.Interfaces;
using HudhudNestApi.Domain.Search.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Repositories;

namespace HudhudNestApi.Infrastructure.Search;

/// <summary>
/// Phase-0, Task 3 — periodically re-runs every SavedSearch's filter against
/// newly published properties and notifies the owner about fresh matches.
///
/// Reuses PropertyRepository.ApplyFilter (the exact same filter-building logic
/// GetPagedAsync uses for manual search) instead of re-implementing matching
/// rules here — a saved search alert must never disagree with what a live search
/// for the same criteria would return.
///
/// Pattern mirrors the other recurring hosted services (e.g. PhoneVerificationHostedService):
/// IServiceScopeFactory + a polling loop, because DbContext/scoped services cannot be
/// injected directly into a singleton BackgroundService.
/// </summary>
public sealed class SavedSearchMatchHostedService : BackgroundService
{
    private static readonly TimeSpan MatchInterval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SavedSearchMatchHostedService> _logger;

    public SavedSearchMatchHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<SavedSearchMatchHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Saved search match service started. Interval: {Interval}.", MatchInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Saved search matching run failed.");
            }

            await Task.Delay(MatchInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Guarded by a Postgres advisory lock (BackgroundJobLockKeys.SavedSearchMatch): every
    /// deployed instance runs this same timer against the same saved searches, and without
    /// coordination two instances would notify the same owner of the same match twice. See
    /// B-6 in RELEASE-BLOCKERS-AR.md.
    /// </summary>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var savedSearches = scope.ServiceProvider.GetRequiredService<ISavedSearchRepository>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await BackgroundJobLock.TryRunAsync(
                db.Database.GetDbConnection(),
                BackgroundJobLockKeys.SavedSearchMatch,
                nameof(SavedSearchMatchHostedService),
                _logger,
                async () =>
                {
                    var allSearches = await savedSearches.GetAllAsync(ct);
                    var now = DateTime.UtcNow;
                    var matchedCount = 0;

                    foreach (var savedSearch in allSearches)
                    {
                        // First run for a search only looks forward from its creation — never
                        // floods the owner with every pre-existing listing that already matched.
                        var since = savedSearch.LastMatchedAt ?? savedSearch.CreatedAt;

                        var filter = ToFilterDto(savedSearch);
                        var query = PropertyRepository.ApplyFilter(db.Properties.AsNoTracking(), filter,
                            await PropertyRepository.ResolveLocationFallbackAsync(db, filter, ct))
                            .Where(p => p.CreatedAt > since);

                        var matches = await query
                            .OrderByDescending(p => p.CreatedAt)
                            .Take(10) // Cap per run — an unusually broad saved search should not spam the user.
                            .ToListAsync(ct);

                        if (matches.Count == 0)
                            continue;

                        foreach (var property in matches)
                        {
                            await notifications.NotifySavedSearchMatchAsync(
                                savedSearch.UserId,
                                property.Id,
                                property.Title,
                                savedSearch.Name ?? "بحث محفوظ",
                                ct);
                        }

                        // savedSearch came from ISavedSearchRepository.GetAllAsync(), which
                        // returns tracked entities specifically so this mutation is picked up
                        // by the single SaveChangesAsync call below — no explicit Update()
                        // call needed.
                        savedSearch.MarkMatched();
                        matchedCount += matches.Count;
                    }

                    await db.SaveChangesAsync(ct);

                    if (matchedCount > 0)
                    {
                        _logger.LogInformation("Saved search run notified {Count} new matches across {SearchCount} searches.", matchedCount, allSearches.Count);
                    }
                },
                ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static PropertyFilterDto ToFilterDto(SavedSearch s) => new()
    {
        Page = 1,
        PageSize = 10,
        CountryCode = s.CountryCode,
        City = s.City,
        Region = s.Region,
        GovernorateId = s.GovernorateId,
        DistrictId = s.DistrictId,
        NeighborhoodId = s.NeighborhoodId,
        PropertyTypeId = s.PropertyTypeId,
        ListingType = s.ListingType,
        MinPrice = s.MinPrice,
        MaxPrice = s.MaxPrice,
        CurrencyCode = s.CurrencyCode,
        MinRooms = s.MinRooms,
        MaxRooms = s.MaxRooms,
        MinArea = s.MinArea,
        MaxArea = s.MaxArea
    };
}
