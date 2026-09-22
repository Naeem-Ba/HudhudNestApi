using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.ShortStay.Interfaces;

namespace HudhudNestApi.Infrastructure.Listings;

/// <summary>
/// Sums active listings across every listing type that draws from the shared plan quota —
/// see <see cref="IActiveListingCounter"/>'s doc comment for why this exists as its own seam
/// instead of living inside PropertyRepository.
/// </summary>
internal sealed class ActiveListingCounter : IActiveListingCounter
{
    private readonly IPropertyRepository _properties;
    private readonly IShortStayListingRepository _shortStayListings;
    private readonly IAgencyRepository _agencies;

    public ActiveListingCounter(
        IPropertyRepository properties,
        IShortStayListingRepository shortStayListings,
        IAgencyRepository agencies)
    {
        _properties = properties;
        _shortStayListings = shortStayListings;
        _agencies = agencies;
    }

    public async Task<int> CountActiveListingsByOwnerAsync(Guid ownerId, CancellationToken ct = default)
    {
        var propertyCount = await _properties.CountActiveListingsByOwnerAsync(ownerId, ct);
        var shortStayCount = await _shortStayListings.CountActiveByOwnerAsync(ownerId, ct);
        return propertyCount + shortStayCount;
    }

    public async Task<int> CountActiveListingsByAgencyAsync(Guid agencyId, CancellationToken ct = default)
    {
        var propertyCount = await _properties.CountActiveListingsByAgencyAsync(agencyId, ct);

        // ShortStayListing carries no AgencyId column (only Property does — see
        // IActiveListingCounter's doc comment), so the agency's Short-Stay total is resolved
        // by summing each member's own listings instead of a direct column filter.
        var members = await _agencies.GetMembersAsync(agencyId, ct);
        var memberIds = members.Select(m => m.Id).ToList();
        var shortStayCount = await _shortStayListings.CountActiveByOwnerIdsAsync(memberIds, ct);

        return propertyCount + shortStayCount;
    }
}
