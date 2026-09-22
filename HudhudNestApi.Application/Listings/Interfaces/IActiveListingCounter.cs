using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Listings.Interfaces;

/// <summary>
/// Counts an owner's (or agency's, pooled) active listings across every listing type that
/// draws from the same <see cref="IListingQuotaPolicy"/> quota — Property (Sale/Rent) and
/// ShortStayListing today. Introduced because <c>IPropertyRepository.CountActiveListingsByOwnerAsync</c>
/// only ever counted Property rows: a Short-Stay listing consumed none of the owner's plan
/// quota, so an owner could hold an unlimited number of them regardless of plan. See
/// BACKEND-ISSUES.md-style note on <see cref="IListingQuotaPolicy"/> for the quota-limit side
/// of this — this interface is the count side, kept separate so a new listing type only needs
/// a new branch here rather than a change to the quota-limit resolution itself.
///
/// Both <see cref="CreatePropertyCommandHandler"/> and <c>CreateShortStayListingCommandHandler</c>
/// must read the SAME total through this interface before comparing against
/// <see cref="IListingQuotaPolicy.GetActiveListingLimitAsync"/> — otherwise the two creation
/// paths would each police a different, smaller pool than the one actually being consumed.
/// </summary>
public interface IActiveListingCounter
{
    /// <summary>
    /// Total active listings (every counted type, summed) owned directly by
    /// <paramref name="ownerId"/> — not agency-pooled. Same "active" definition each
    /// underlying count already used: excludes soft-deleted rows and Property's Expired
    /// status; Short-Stay has no Expired-equivalent yet, so every non-deleted Short-Stay
    /// listing (published or draft) counts, mirroring how an unpublished Property draft
    /// already counts today.
    /// </summary>
    Task<int> CountActiveListingsByOwnerAsync(Guid ownerId, CancellationToken ct = default);

    /// <summary>
    /// Total active listings (every counted type, summed) across every member of the given
    /// agency, pooled together — mirrors <see cref="IPropertyRepository.CountActiveListingsByAgencyAsync"/>'s
    /// semantics but also folds in each member's Short-Stay listings, resolved through
    /// <see cref="IAgencyRepository.GetMembersAsync"/> since ShortStayListing carries no
    /// AgencyId column of its own (only Property does).
    /// </summary>
    Task<int> CountActiveListingsByAgencyAsync(Guid agencyId, CancellationToken ct = default);
}
