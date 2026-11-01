using HudhudNestApi.Application.Listings;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HudhudNestApi.Infrastructure.Listings;

/// <summary>
/// Growth engine P1 (DECISIONS.md D-06): tells the users who saved a listing that its price changed.
/// Runs in the background so the owner's edit request never waits for the fan-out. Rows younger than
/// <see cref="SettleTime"/> wait for the next sweep, so an owner correcting a typo produces one
/// notification with the net change instead of several. The owner is never a recipient here (they
/// already get the existing confirmation). Guarded by an advisory lock like the other sweeps; the
/// dedupe key per history row keeps a retried sweep from notifying twice.
/// </summary>
public sealed class PriceChangeFollowerNotificationHostedService : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan SettleTime = TimeSpan.FromMinutes(5);
    internal const int MaxListingsPerSweep = 500;
    internal const int MaxFollowersPerListing = 5000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PriceChangeFollowerNotificationHostedService> _logger;

    public PriceChangeFollowerNotificationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<PriceChangeFollowerNotificationHostedService> logger)
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
                _logger.LogError(ex, "Price change follower sweep failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    internal async Task<int> RunOnceAsync(DateTime nowUtc, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var sent = 0;

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await BackgroundJobLock.TryRunAsync(
                db.Database.GetDbConnection(),
                BackgroundJobLockKeys.PriceChangeFollowers,
                nameof(PriceChangeFollowerNotificationHostedService),
                _logger,
                async () => sent = await SweepAsync(db, notifications, nowUtc, ct),
                ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return sent;
    }

    private async Task<int> SweepAsync(AppDbContext db, INotificationService notifications, DateTime nowUtc, CancellationToken ct)
    {
        var settledBefore = nowUtc - SettleTime;

        // Whole listings only: every pending row of a picked listing is handled together, so the net
        // change is computed over all of them and no row is left behind to announce a stale change.
        var propertyIds = await db.PropertyPriceHistories
            .Where(h => h.FollowersNotifiedAt == null && h.ChangedAt <= settledBefore)
            .GroupBy(h => h.PropertyId)
            .Select(g => new { PropertyId = g.Key, First = g.Min(h => h.ChangedAt) })
            .OrderBy(x => x.First)
            .Take(MaxListingsPerSweep)
            .Select(x => x.PropertyId)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var propertyId in propertyIds)
        {
            var rows = await db.PropertyPriceHistories
                .Where(h => h.PropertyId == propertyId && h.FollowersNotifiedAt == null && h.ChangedAt <= settledBefore)
                .ToListAsync(ct);

            var listing = await db.Properties
                .AsNoTracking()
                .Where(p => p.Id == propertyId)
                .Where(Property.OnMarket(nowUtc))
                .Select(p => new { p.Title, p.OwnerId })
                .FirstOrDefaultAsync(ct);

            // A listing that left the market meanwhile announces nothing; its rows are closed anyway.
            var summary = listing is null ? null : PriceChangeDigest.Summarize(rows);
            if (listing is not null && summary is not null)
            {
                var followers = await db.Favorites
                    .AsNoTracking()
                    .Where(f => f.PropertyId == propertyId && f.UserId != listing.OwnerId)
                    .OrderBy(f => f.CreatedAt)
                    .Select(f => f.UserId)
                    .Take(MaxFollowersPerListing)
                    .ToListAsync(ct);

                foreach (var userId in followers)
                {
                    await notifications.NotifyFavoritePriceChangedAsync(
                        userId, propertyId, listing.Title, summary.Detail, summary.AnnouncedRowId, ct);
                    sent++;
                }

                if (followers.Count == MaxFollowersPerListing)
                {
                    _logger.LogWarning(
                        "Price change for {PropertyId} reached the follower cap {Cap}; later followers were not notified.",
                        propertyId, MaxFollowersPerListing);
                }
            }

            foreach (var row in rows)
                row.MarkFollowersNotified(nowUtc);

            await db.SaveChangesAsync(ct);
        }

        if (sent > 0)
            _logger.LogInformation("Price change follower sweep sent {Count} notifications for {Listings} listings.", sent, propertyIds.Count);

        return sent;
    }
}
