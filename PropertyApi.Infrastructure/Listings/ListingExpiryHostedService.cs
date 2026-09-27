using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Events;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Listings;

/// <summary>
/// Enforces the listing publication window that the public pricing page promises:
/// one month of visibility, a warning before it ends, then expiry, then deletion
/// after a grace period unless the owner pays to extend.
///
/// Why this service has to exist even though search already hides expired listings:
/// PropertyRepository filters on <c>ExpiresAt > now</c>, so an expired listing silently
/// stops appearing. Silently is the problem. Before this service, the owner was never
/// told, the listing's Status still read Available, and nothing ever cleaned it up — a
/// listing could sit invisible and undeleted forever. The date filter makes a listing
/// invisible; this service makes expiry an event the owner can see and act on.
///
/// Pattern mirrors SavedSearchMatchHostedService: IServiceScopeFactory + a polling loop,
/// because AppDbContext and other scoped services cannot be injected into a singleton
/// BackgroundService.
/// </summary>
public sealed class ListingExpiryHostedService : BackgroundService
{
    /// <summary>
    /// Every transition here is measured in days, so sweeping more often than a few times
    /// a day buys nothing and just adds database load. Six hours keeps the worst-case lag
    /// between a listing expiring and its owner being told well under a day.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);

    /// <summary>
    /// Cap per phase per run. A backlog (first deployment against existing data, or a long
    /// outage) must drain over several sweeps rather than opening one enormous transaction
    /// and firing thousands of notifications at once.
    /// </summary>
    private const int MaxItemsPerPhase = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ListingExpiryHostedService> _logger;

    public ListingExpiryHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<ListingExpiryHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Listing expiry service started. Interval={Interval}, PublicationPeriod={Period}, Grace={Grace}.",
            SweepInterval,
            ListingLifecyclePolicy.PublicationPeriod,
            ListingLifecyclePolicy.GracePeriodBeforeDeletion);

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
                _logger.LogError(ex, "Listing expiry sweep failed.");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }

    /// <summary>
    /// One full sweep. Internal so it can be driven directly from a test without
    /// waiting on the timer.
    ///
    /// Guarded by a Postgres advisory lock (BackgroundJobLockKeys.ListingExpiry): every
    /// deployed instance runs this same timer, and the deletion phase in particular must run
    /// exactly once per sweep — two instances racing MaxItemsPerPhase-capped queries would
    /// pick the same rows, not different ones. See B-6 in RELEASE-BLOCKERS-AR.md.
    /// </summary>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var mediaStorage = scope.ServiceProvider.GetRequiredService<IMediaStorageService>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await BackgroundJobLock.TryRunAsync(
                db.Database.GetDbConnection(),
                BackgroundJobLockKeys.ListingExpiry,
                nameof(ListingExpiryHostedService),
                _logger,
                async () =>
                {
                    var now = DateTime.UtcNow;

                    // Order matters: warn before expiring, and expire before deleting, so a
                    // listing cannot cross two boundaries in one sweep without its owner
                    // hearing about the first one.
                    var warned = await WarnExpiringSoonAsync(db, notifications, now, ct);
                    var expired = await ExpireElapsedAsync(db, notifications, publisher, now, ct);
                    var deleted = await DeleteAfterGraceAsync(db, mediaStorage, now, ct);

                    // Independent of the three phases above and ordered last only because it
                    // is the cheapest: a listing can lose its featured placement while its
                    // publication window is still running, and expiring can happen while a
                    // placement is still paid for.
                    var unfeatured = await ClearElapsedFeaturedAsync(db, now, ct);

                    if (warned + expired + deleted + unfeatured > 0)
                    {
                        _logger.LogInformation(
                            "Listing expiry sweep complete. Warned={Warned}, Expired={Expired}, Deleted={Deleted}, Unfeatured={Unfeatured}.",
                            warned,
                            expired,
                            deleted,
                            unfeatured);
                    }
                },
                ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Phase 1 — warn owners whose listing is inside the warning window and who have not
    /// already been warned for this publication period.
    /// </summary>
    private static async Task<int> WarnExpiringSoonAsync(
        AppDbContext db,
        INotificationService notifications,
        DateTime now,
        CancellationToken ct)
    {
        var warningThreshold = now.Add(ListingLifecyclePolicy.ExpiryWarningLeadTime);

        var candidates = await db.Properties
            .Where(p =>
                p.IsPublished &&
                p.Status != PropertyStatus.Expired &&
                p.ExpiresAt != null &&
                p.ExpiresAt > now &&
                p.ExpiresAt <= warningThreshold &&
                p.ExpiryWarningSentAt == null)
            .OrderBy(p => p.ExpiresAt)
            .Take(MaxItemsPerPhase)
            .ToListAsync(ct);

        foreach (var property in candidates)
        {
            var daysRemaining = (int)Math.Ceiling((property.ExpiresAt!.Value - now).TotalDays);

            await notifications.NotifyListingExpiringSoonAsync(
                property.OwnerId,
                property.Id,
                property.Title,
                daysRemaining,
                ct);

            property.MarkExpiryWarningSent();
        }

        await db.SaveChangesAsync(ct);
        return candidates.Count;
    }

    /// <summary>
    /// Phase 2 — take elapsed listings out of publication and tell the owner, including how
    /// long they have to recover it.
    /// </summary>
    private async Task<int> ExpireElapsedAsync(
        AppDbContext db,
        INotificationService notifications,
        IPublisher publisher,
        DateTime now,
        CancellationToken ct)
    {
        var candidates = await db.Properties
            .Where(p =>
                p.Status != PropertyStatus.Expired &&
                p.ExpiresAt != null &&
                p.ExpiresAt <= now)
            .OrderBy(p => p.ExpiresAt)
            .Take(MaxItemsPerPhase)
            .ToListAsync(ct);

        // Captured before MarkExpired() overwrites Status — SocialDistribution needs the actual
        // FROM status (Available/Reserved/...) to decide what a live publication should do (Phase
        // 1 audit F-10: this sweep never told it a listing expired at all).
        var statusBeforeExpiry = candidates.ToDictionary(p => p.Id, p => p.Status);

        foreach (var property in candidates)
        {
            property.MarkExpired();

            var graceDaysRemaining = (int)Math.Ceiling(
                (ListingLifecyclePolicy.DeletionDueAt(property.ExpiresAt!.Value) - now).TotalDays);

            await notifications.NotifyListingExpiredAsync(
                property.OwnerId,
                property.Id,
                property.Title,
                graceDaysRemaining,
                ct);
        }

        await db.SaveChangesAsync(ct);

        // Fire-and-notify, same contract as every other PropertyStatusChangedEvent publisher: a
        // broken subscriber must never fail (or slow down) this sweep — already-committed expiry
        // must not be undone by a downstream failure.
        foreach (var property in candidates)
        {
            try
            {
                await publisher.Publish(
                    new PropertyStatusChangedEvent(property.Id, statusBeforeExpiry[property.Id], PropertyStatus.Expired, now),
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish PropertyStatusChangedEvent after expiring listing {PropertyId}.", property.Id);
            }
        }

        return candidates.Count;
    }

    /// <summary>
    /// Phase 3 — delete listings whose grace period has run out.
    /// </summary>
    /// <remarks>
    /// Soft delete for the Property row itself. Property.MarkExpiredListingDeletedBySystem()
    /// sets IsDeleted, which the global query filter honours everywhere, so the listing
    /// disappears from every read path while the row survives. Nothing in this codebase
    /// physically removes a Property, and an unattended sweep is the last place to start.
    ///
    /// The listing's Cloudinary image binaries are a different story on purpose: they are
    /// remote, external, and not covered by the soft-delete query filter at all — before
    /// this fix nothing anywhere ever deleted them, so every listing that ever expired left
    /// its photos orphaned in cloud storage forever (docs/privacy/privacy-gaps.md, P1).
    /// This phase now removes them for real (best-effort — a Cloudinary failure for one
    /// image must not block the sweep or the rest of the images), even though the Property
    /// row itself stays soft-deleted like everything else.
    ///
    /// No notification is sent here on purpose: the owner was already told at expiry, with
    /// the deadline stated. A second message whose only content is "it is now gone" arrives
    /// too late to be acted on.
    /// </remarks>
    private async Task<int> DeleteAfterGraceAsync(
        AppDbContext db,
        IMediaStorageService mediaStorage,
        DateTime now,
        CancellationToken ct)
    {
        var graceCutoff = now.Subtract(ListingLifecyclePolicy.GracePeriodBeforeDeletion);

        var candidates = await db.Properties
            .Include(p => p.Images)
            .Where(p =>
                p.Status == PropertyStatus.Expired &&
                p.ExpiresAt != null &&
                p.ExpiresAt <= graceCutoff)
            .OrderBy(p => p.ExpiresAt)
            .Take(MaxItemsPerPhase)
            .ToListAsync(ct);

        var imagePublicIds = candidates
            .SelectMany(p => p.Images)
            .Where(image => !string.IsNullOrWhiteSpace(image.PublicId))
            .Select(image => image.PublicId!)
            .ToList();

        foreach (var property in candidates)
        {
            property.MarkExpiredListingDeletedBySystem();
        }

        await db.SaveChangesAsync(ct);

        foreach (var publicId in imagePublicIds)
        {
            try
            {
                await mediaStorage.DeleteImageAsync(publicId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete Cloudinary image {PublicId} during the listing-expiry sweep.",
                    publicId);
            }
        }

        return candidates.Count;
    }

    /// <summary>
    /// Phase 4 — end featured placements whose paid window has elapsed.
    /// </summary>
    /// <remarks>
    /// Read paths do not depend on this running: Property.IsCurrentlyFeatured(now) compares
    /// FeaturedUntil itself, so a listing stops being promoted the moment its window ends
    /// regardless of when the sweep next runs. This phase exists to keep the stored flag
    /// honest — an IsFeatured column that stays true for weeks after the placement lapsed
    /// makes every report and every index built on it wrong.
    ///
    /// FeaturedUntil is deliberately left in place. It is the record of what was paid for
    /// and when it ran out; clearing it would erase the only trace of a completed purchase
    /// outside the Transactions table.
    ///
    /// No notification is sent. Unlike listing expiry, nothing is lost that the owner must
    /// act on within a deadline — the listing itself is untouched and still published.
    /// </remarks>
    private static async Task<int> ClearElapsedFeaturedAsync(
        AppDbContext db,
        DateTime now,
        CancellationToken ct)
    {
        var candidates = await db.Properties
            .Where(p =>
                p.IsFeatured &&
                (p.FeaturedUntil == null || p.FeaturedUntil <= now))
            .OrderBy(p => p.FeaturedUntil)
            .Take(MaxItemsPerPhase)
            .ToListAsync(ct);

        foreach (var property in candidates)
        {
            property.ClearFeatured();
        }

        await db.SaveChangesAsync(ct);
        return candidates.Count;
    }
}
