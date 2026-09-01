using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Domain.ShortStay.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.ShortStay;

public sealed class RoomTypeRepository : IRoomTypeRepository
{
    private readonly AppDbContext _db;
    public RoomTypeRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(RoomType roomType, CancellationToken ct = default)
        => await _db.ShortStayRoomTypes.AddAsync(roomType, ct);

    // Always includes the parent ShortStayListing — every caller needs it for the
    // OwnerId ownership check (see AddAccommodationUnit/SetPricingRules/SetMinimumStayRules handlers).
    public async Task<RoomType?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.ShortStayRoomTypes
            .Include(rt => rt.ShortStayListing)
            .FirstOrDefaultAsync(rt => rt.Id == id, ct);

    public async Task<RoomType?> GetByIdWithPricingAsync(Guid id, CancellationToken ct = default)
        => await _db.ShortStayRoomTypes
            .Include(rt => rt.ShortStayListing)
            .Include(rt => rt.PricingRules)
            .Include(rt => rt.MinimumStayRules)
            .Include(rt => rt.Units)
            .FirstOrDefaultAsync(rt => rt.Id == id, ct);

    public async Task<IReadOnlyList<RoomType>> GetByListingIdAsync(Guid shortStayListingId, CancellationToken ct = default)
        => await _db.ShortStayRoomTypes
            .Include(rt => rt.Units)
            .Where(rt => rt.ShortStayListingId == shortStayListingId)
            .ToListAsync(ct);
}

public sealed class AccommodationUnitRepository : IAccommodationUnitRepository
{
    private readonly AppDbContext _db;
    public AccommodationUnitRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(AccommodationUnit unit, CancellationToken ct = default)
        => await _db.AccommodationUnits.AddAsync(unit, ct);

    public async Task<AccommodationUnit?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.AccommodationUnits.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<AccommodationUnit?> GetByIdWithListingAsync(Guid id, CancellationToken ct = default)
        => await _db.AccommodationUnits
            .Include(u => u.RoomType)
                .ThenInclude(rt => rt.ShortStayListing)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
}
