using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Agencies.Interfaces;

/// <summary>
/// Persistence seam for agencies and their membership.
///
/// Membership lives on UserAccount.AgencyId rather than in a join table, so the member
/// operations here are UserAccount reads and writes. That is the shape the chosen model
/// implies: one user belongs to at most one agency at a time, and there is no per-member
/// state beyond "which agency, since when".
/// </summary>
public interface IAgencyRepository
{
    Task<Agency?> GetByIdAsync(Guid agencyId, CancellationToken ct = default);

    /// <summary>Resolves an agency by its public slug. Used by the public agency page.</summary>
    Task<Agency?> GetBySlugAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// True when the slug is already taken by another (non-deleted) agency. Checked before
    /// insert so a collision surfaces as a 409 rather than as a unique-index violation
    /// wrapped in a 500.
    /// </summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);

    /// <summary>The agency this user owns, if any. A user may own at most one.</summary>
    Task<Agency?> GetByOwnerAsync(Guid ownerUserId, CancellationToken ct = default);

    Task AddAsync(Agency agency, CancellationToken ct = default);

    void Update(Agency agency);

    Task<UserAccount?> GetUserAccountAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Members of an agency, ordered by join date. Includes the owner — the owner is a
    /// member with extra rights, not a separate kind of record.
    /// </summary>
    Task<IReadOnlyList<UserAccount>> GetMembersAsync(
        Guid agencyId,
        CancellationToken ct = default);

    /// <summary>
    /// How many users currently belong to the agency. Checked against Agency.MaxMembers
    /// before adding, so the cap is enforced on the write rather than discovered on a read.
    /// </summary>
    Task<int> CountMembersAsync(Guid agencyId, CancellationToken ct = default);

    /// <summary>
    /// Clears AgencyId on every listing attributed to this agency.
    ///
    /// Used when an agency is deleted. The listings themselves are untouched: they belong
    /// to their owners, stay published, and simply stop showing an agency badge. Anything
    /// else would let deleting an organisation destroy individuals' listings.
    /// </summary>
    Task<int> ClearAgencyAttributionAsync(Guid agencyId, CancellationToken ct = default);

    /// <summary>
    /// Active agencies matching exactly ONE location scope, excluding ids already selected.
    ///
    /// Added for Valuation Stage 4 (Office Matching)'s progressive geographic expansion:
    /// the caller runs this once per level — Neighborhood, then District, then Governorate,
    /// then a set of neighboring Governorates — passing only that level's parameter and
    /// leaving the others null, so each call is a single, narrow, database-side query
    /// instead of loading every agency into memory and filtering in-process. Pass exactly
    /// one of <paramref name="neighborhoodId"/>/<paramref name="districtId"/>/
    /// <paramref name="governorateIds"/> per call; mixing them is not supported by this
    /// method (there was no existing agency-search method or Specification to reuse for
    /// this — see OfficeMatchingService's own inspection notes).
    /// </summary>
    Task<IReadOnlyList<Agency>> FindActiveByLocationAsync(
        int? neighborhoodId,
        int? districtId,
        IReadOnlyCollection<int>? governorateIds,
        IReadOnlyCollection<Guid> excludeAgencyIds,
        CancellationToken ct = default);
}
