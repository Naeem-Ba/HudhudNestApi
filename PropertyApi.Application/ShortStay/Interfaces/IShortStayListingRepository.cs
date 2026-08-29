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

    void Update(ShortStayListing listing);
}
