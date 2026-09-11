using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Application.ShortStay.Interfaces;

/// <summary>Per-aggregate repository — no generic IRepository&lt;T&gt; exists in this codebase.</summary>
public interface IShortStayListingRepository
{
    Task AddAsync(ShortStayListing listing, CancellationToken ct = default);

    /// <summary>DB-driven search/filter/paging — mirrors PropertyRepository's ApplyFilter
    /// approach (a reusable, composable predicate chain translated entirely to SQL).</summary>
    Task<PagedResult<ShortStayListing>> SearchAsync(ShortStayListingSearchFilter filter, CancellationToken ct = default);

    /// <summary>Loads the aggregate with RoomTypes/Units/Amenities/Photos for owner-facing reads and mutation.</summary>
    Task<ShortStayListing?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default);

    Task<ShortStayListing?> GetPublishedByIdWithDetailsAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<ShortStayListing>> GetByOwnerAsync(Guid ownerId, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// This owner's listings that consume their plan quota. No Expired-equivalent exists for
    /// Short-Stay yet (unlike Property), so every non-deleted listing counts, published or
    /// not — mirrors Property's "unpublished drafts DO count" rule. Deleted listings are
    /// excluded by the global query filter on ShortStayListing.
    /// </summary>
    Task<int> CountActiveByOwnerAsync(Guid ownerId, CancellationToken ct = default);

    /// <summary>
    /// Same "active" definition as <see cref="CountActiveByOwnerAsync"/>, summed across every
    /// owner id in <paramref name="ownerIds"/> in one query — used for agency-pooled quota
    /// counting, where the caller resolves the member id list via IAgencyRepository first
    /// (ShortStayListing has no AgencyId column of its own).
    /// </summary>
    Task<int> CountActiveByOwnerIdsAsync(IReadOnlyCollection<Guid> ownerIds, CancellationToken ct = default);

    void Update(ShortStayListing listing);

    /// <summary>Soft delete — repository just tracks, UnitOfWork saves. Caller must have
    /// already invoked <see cref="ShortStayListing.MarkAsDeleted"/>.</summary>
    void Remove(ShortStayListing listing);
}
