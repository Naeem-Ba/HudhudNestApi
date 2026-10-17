using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Listings.Services;

/// <summary>
/// Enforces the active-listing limit - plan-driven for both an independent owner and an
/// agency-pooled owner (BACKEND-ISSUES.md section B-3): <see cref="IListingQuotaPolicy"/> resolves the
/// limit from <c>Plan.ListingLimit</c> via <c>UserAccount.PlanId</c> (the agency owner's, when the
/// account belongs to one), never from a constant here.
///
/// The count comes from <see cref="IActiveListingCounter"/> - a shared total across every listing type
/// that draws from this quota (Property + ShortStayListing today), so each type is not checked against a
/// separate, smaller pool than the one the plan promises.
///
/// An owner (or agency) who already holds more than the limit - e.g. after a plan downgrade - keeps every
/// listing; nothing is removed retroactively. They simply cannot add another until they are back under it.
/// </summary>
public sealed class ListingQuotaGuard : IListingQuotaGuard
{
    private readonly IListingQuotaPolicy _quotaPolicy;
    private readonly IActiveListingCounter _activeListingCounter;
    private readonly ILogger<ListingQuotaGuard> _logger;

    public ListingQuotaGuard(
        IListingQuotaPolicy quotaPolicy,
        IActiveListingCounter activeListingCounter,
        ILogger<ListingQuotaGuard> logger)
    {
        _quotaPolicy = quotaPolicy;
        _activeListingCounter = activeListingCounter;
        _logger = logger;
    }

    public async Task EnsureCapacityAsync(UserAccount ownerAccount, CancellationToken ct = default)
    {
        var limit = await _quotaPolicy.GetActiveListingLimitAsync(ownerAccount, ct);

        if (ownerAccount.AgencyId is { } agencyId)
        {
            var agencyActiveListings = await _activeListingCounter.CountActiveListingsByAgencyAsync(agencyId, ct);

            if (agencyActiveListings < limit)
                return;

            _logger.LogInformation(
                "Listing blocked by agency-pooled quota. AgencyId={AgencyId}, Active={Active}, Limit={Limit}",
                agencyId,
                agencyActiveListings,
                limit);

            throw new ConflictException(
                $"بلغ مكتبكم الحدّ الأقصى المسموح به من الإعلانات النشطة ({limit} إعلاناً) وفق خطة مكتبكم. " +
                "احذفوا إعلاناً قائماً أو رقّوا الخطة لإضافة إعلان جديد.");
        }

        var activeListings = await _activeListingCounter.CountActiveListingsByOwnerAsync(ownerAccount.Id, ct);

        if (activeListings < limit)
            return;

        _logger.LogInformation(
            "Listing blocked by plan quota. OwnerId={OwnerId}, Active={Active}, Limit={Limit}",
            ownerAccount.Id,
            activeListings,
            limit);

        throw new ConflictException(
            $"خطتكم الحالية تسمح بـ {limit} إعلاناً نشطاً كحد أقصى. لديكم حالياً {activeListings}. " +
            "احذفوا إعلاناً قائماً أو رقّوا خطتكم لإضافة إعلان جديد.");
    }
}
