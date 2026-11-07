using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Users.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Plans;

/// <summary>See <see cref="IAgencyListingVisibility"/>. The daily AgencyPlanSuspensionHostedService stays the safety net.</summary>
internal sealed class AgencyListingVisibilityService : IAgencyListingVisibility
{
    private readonly AppDbContext _db;
    private readonly ILogger<AgencyListingVisibilityService> _logger;

    public AgencyListingVisibilityService(AppDbContext db, ILogger<AgencyListingVisibilityService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task RestoreForRenewedOwnerAsync(Guid ownerUserId, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - AgencyPlanSuspensionHostedService.GracePeriod;

        // Still past the grace period (renewal did not bring the plan back): nothing to lift.
        var stillSuspended = await _db.UserAccounts.AsNoTracking().AnyAsync(a =>
            a.Id == ownerUserId
            && a.PlanId != null
            && a.PlanStatus == PlanStatus.Active
            && a.PlanExpiresAt != null
            && a.PlanExpiresAt <= cutoff, ct);
        if (stillSuspended)
            return;

        var agencyIds = await _db.Agencies.AsNoTracking()
            .Where(a => a.OwnerUserId == ownerUserId)
            .Select(a => a.Id)
            .ToListAsync(ct);
        if (agencyIds.Count == 0)
            return;

        var memberIds = await _db.UserAccounts.AsNoTracking()
            .Where(a => a.AgencyId != null && agencyIds.Contains(a.AgencyId.Value))
            .Select(a => a.Id)
            .ToListAsync(ct);

        var reason = Property.AgencyPlanExpiredTakeDownReason;

        var properties = await _db.Properties
            .Where(p => p.TakenDownReason == reason
                        && ((p.AgencyId != null && agencyIds.Contains(p.AgencyId.Value)) || memberIds.Contains(p.OwnerId)))
            .ToListAsync(ct);
        foreach (var property in properties)
            property.RestoreFromTakeDown();

        var shortStay = await _db.ShortStayListings
            .Where(l => l.TakenDownReason == reason && memberIds.Contains(l.OwnerId))
            .ToListAsync(ct);
        foreach (var listing in shortStay)
            listing.RestoreFromTakeDown();

        if (properties.Count + shortStay.Count == 0)
            return;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Office listings restored after plan renewal. OwnerId={OwnerId}, Properties={Properties}, ShortStay={ShortStay}.",
            ownerUserId, properties.Count, shortStay.Count);
    }
}
