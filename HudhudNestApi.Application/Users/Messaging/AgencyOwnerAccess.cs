using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Users.Messaging;

/// <summary>
/// Business rule: the owner of the agency a listing belongs to may READ the conversations about
/// that listing (read-only oversight of their members' inquiries). The agency owner is also the
/// agency manager: there is no separate manager role.
/// </summary>
public static class AgencyOwnerAccess
{
    public static async Task<bool> IsOwnerOfListingAgencyAsync(
        IAgencyRepository agencies,
        Property property,
        Guid userId,
        CancellationToken ct)
    {
        if (property.AgencyId is not { } agencyId)
            return false;

        var agency = await agencies.GetByIdAsync(agencyId, ct);
        return agency is { IsDeleted: false } && agency.OwnerUserId == userId;
    }
}
