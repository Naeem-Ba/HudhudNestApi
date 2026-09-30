using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Application.ShortStay.Interfaces;

public interface IRoomTypeRepository
{
    Task AddAsync(RoomType roomType, CancellationToken ct = default);
    Task<RoomType?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RoomType?> GetByIdWithPricingAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<RoomType>> GetByListingIdAsync(Guid shortStayListingId, CancellationToken ct = default);
}

public interface IAccommodationUnitRepository
{
    Task AddAsync(AccommodationUnit unit, CancellationToken ct = default);
    Task<AccommodationUnit?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Loads the unit together with its owning RoomType/Listing — needed to resolve
    /// ownership (host) and pricing without a second round-trip.</summary>
    Task<AccommodationUnit?> GetByIdWithListingAsync(Guid id, CancellationToken ct = default);
}
