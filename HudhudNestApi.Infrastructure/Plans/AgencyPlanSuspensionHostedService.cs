using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Users.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Plans;

/// <summary>
/// Product decision 2026-11-07: an office whose owner's paid plan has been expired for longer than the 30-day grace period
/// disappears from the public, reversibly. Its listings (those of the office and of its members) are taken down with the
/// fixed reason <see cref="Property.AgencyPlanExpiredTakeDownReason"/>; when the owner renews, exactly those take-downs are
/// lifted. A listing an administrator took down for another reason is never touched, and nothing is deleted.
///
/// During the grace period itself nothing is hidden: the office cannot add listings (ListingQuotaPolicy) but keeps receiving
/// messages, enquiries and appointments. The hiding sweep runs once every 24 hours (idempotent, advisory lock). A renewal does not
/// wait for it: the admin plan operations call <see cref="IAgencyListingVisibility"/> at once; this sweep's restore phase is only
/// the safety net.
/// </summary>
public sealed class AgencyPlanSuspensionHostedService : BackgroundService
{
    /// <summary>Days an office stays visible after its owner's plan ends. Keep in step with the app's AGENCY_GRACE_DAYS.</summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromDays(30);

    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);
    private const int MaxListingsPerPhase = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgencyPlanSuspensionHostedService> _logger;

    public AgencyPlanSuspensionHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<AgencyPlanSuspensionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Agency plan suspension service started. Interval={Interval}, Grace={Grace}.", SweepInterval, GracePeriod);

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
                _logger.LogError(ex, "Agency plan suspension sweep failed.");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }

    /// <summary>One sweep, internal so a test can drive it without the timer.</summary>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await BackgroundJobLock.TryRunAsync(
                db.Database.GetDbConnection(),
                BackgroundJobLockKeys.AgencyPlanSuspension,
                nameof(AgencyPlanSuspensionHostedService),
                _logger,
                async () =>
                {
                    var cutoff = DateTime.UtcNow - GracePeriod;
                    var reason = Property.AgencyPlanExpiredTakeDownReason;

                    // Agencies whose owner's plan (stored Active, end date passed — i.e. Expired) ended before the cutoff.
                    var suspendedAgencyIds = await (
                        from agency in db.Agencies.AsNoTracking()
                        join owner in db.UserAccounts.AsNoTracking() on agency.OwnerUserId equals owner.Id
                        where owner.PlanId != null
                              && owner.PlanStatus == PlanStatus.Active
                              && owner.PlanExpiresAt != null
                              && owner.PlanExpiresAt <= cutoff
                        select agency.Id)
                        .ToListAsync(ct);

                    var suspendedMemberIds = suspendedAgencyIds.Count == 0
                        ? new List<Guid>()
                        : await db.UserAccounts.AsNoTracking()
                            .Where(a => a.AgencyId != null && suspendedAgencyIds.Contains(a.AgencyId.Value))
                            .Select(a => a.Id)
                            .ToListAsync(ct);

                    var hidden = 0;
                    if (suspendedAgencyIds.Count > 0)
                    {
                        var properties = await db.Properties
                            .Where(p => p.IsPublished && p.TakenDownAt == null
                                        && ((p.AgencyId != null && suspendedAgencyIds.Contains(p.AgencyId.Value))
                                            || suspendedMemberIds.Contains(p.OwnerId)))
                            .Take(MaxListingsPerPhase)
                            .ToListAsync(ct);
                        foreach (var property in properties)
                            property.TakeDown(property.OwnerId, reason);

                        var shortStay = await db.ShortStayListings
                            .Where(l => l.IsPublished && l.TakenDownAt == null && suspendedMemberIds.Contains(l.OwnerId))
                            .Take(MaxListingsPerPhase)
                            .ToListAsync(ct);
                        foreach (var listing in shortStay)
                            listing.TakeDown(listing.OwnerId, reason);

                        hidden = properties.Count + shortStay.Count;
                    }

                    // Renewed (or the owner changed): lift only what this sweep took down.
                    var restoredProperties = await db.Properties
                        .Where(p => p.TakenDownReason == reason
                                    && !(p.AgencyId != null && suspendedAgencyIds.Contains(p.AgencyId.Value))
                                    && !suspendedMemberIds.Contains(p.OwnerId))
                        .Take(MaxListingsPerPhase)
                        .ToListAsync(ct);
                    foreach (var property in restoredProperties)
                        property.RestoreFromTakeDown();

                    var restoredShortStay = await db.ShortStayListings
                        .Where(l => l.TakenDownReason == reason && !suspendedMemberIds.Contains(l.OwnerId))
                        .Take(MaxListingsPerPhase)
                        .ToListAsync(ct);
                    foreach (var listing in restoredShortStay)
                        listing.RestoreFromTakeDown();

                    await db.SaveChangesAsync(ct);

                    var restored = restoredProperties.Count + restoredShortStay.Count;
                    if (hidden + restored > 0)
                    {
                        _logger.LogInformation(
                            "Agency plan suspension sweep complete. Hidden={Hidden}, Restored={Restored}.", hidden, restored);
                    }
                },
                ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
