using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Domain.ShortStay.Entities;
using PropertyApi.Domain.ShortStay.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.ShortStay;

public sealed class ShortStayListingRepository : IShortStayListingRepository
{
    private readonly AppDbContext _db;
    public ShortStayListingRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(ShortStayListing listing, CancellationToken ct = default)
        => await _db.ShortStayListings.AddAsync(listing, ct);

    private IQueryable<ShortStayListing> WithDetails() => _db.ShortStayListings
        .Include(l => l.AccommodationType)
        .Include(l => l.RoomTypes).ThenInclude(rt => rt.Units)
        .Include(l => l.RoomTypes).ThenInclude(rt => rt.PricingRules)
        .Include(l => l.RoomTypes).ThenInclude(rt => rt.MinimumStayRules)
        .Include(l => l.Photos)
        .Include(l => l.ListingAmenities).ThenInclude(a => a.Amenity);

    public async Task<ShortStayListing?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
        => await WithDetails().FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task<ShortStayListing?> GetPublishedByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
        => await WithDetails().FirstOrDefaultAsync(l => l.Id == id && l.IsPublished, ct);

    public async Task<IReadOnlyList<ShortStayListing>> GetByOwnerAsync(Guid ownerId, CancellationToken ct = default)
        => await WithDetails().Where(l => l.OwnerId == ownerId).OrderByDescending(l => l.CreatedAt).ToListAsync(ct);

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
        => await _db.ShortStayListings.AnyAsync(l => l.Id == id, ct);

    public async Task<int> CountActiveByOwnerAsync(Guid ownerId, CancellationToken ct = default)
        => await _db.ShortStayListings
            .AsNoTracking()
            .CountAsync(l => l.OwnerId == ownerId, ct);

    public async Task<int> CountActiveByOwnerIdsAsync(
        IReadOnlyCollection<Guid> ownerIds, CancellationToken ct = default)
    {
        if (ownerIds.Count == 0)
            return 0;

        return await _db.ShortStayListings
            .AsNoTracking()
            .CountAsync(l => ownerIds.Contains(l.OwnerId), ct);
    }

    public void Update(ShortStayListing listing) => _db.ShortStayListings.Update(listing);

    public void Remove(ShortStayListing listing) => _db.ShortStayListings.Remove(listing);

    /// <summary>
    /// Mirrors PropertyRepository.ApplyFilter's approach: a chain of conditional .Where clauses
    /// translated entirely to SQL, never loaded into memory first. When CheckIn/CheckOut are
    /// both supplied, results are further restricted to listings with at least one active Unit
    /// that has no Reserved/CheckedIn range overlapping the requested dates.
    /// </summary>
    public async Task<PagedResult<ShortStayListing>> SearchAsync(
        ShortStayListingSearchFilter filter, CancellationToken ct = default)
    {
        var query = WithDetails().Where(l => l.IsPublished);

        if (!string.IsNullOrWhiteSpace(filter.City))
        {
            var pattern = $"%{filter.City.Trim()}%";
            // City is the catalog's Arabic governorate name, so also match the governorate's
            // English name: "Damascus" finds a listing stored as "دمشق".
            query = query.Where(l =>
                (l.City != null && EF.Functions.ILike(l.City, pattern))
                || (l.GovernorateId != null && _db.Governorates.Any(g =>
                    g.Id == l.GovernorateId
                    && (EF.Functions.ILike(g.NameEn, pattern) || EF.Functions.ILike(g.NameAr, pattern)))));
        }

        if (filter.AccommodationTypeId.HasValue)
            query = query.Where(l => l.AccommodationTypeId == filter.AccommodationTypeId.Value);

        if (filter.Guests.HasValue)
            query = query.Where(l => l.Capacity >= filter.Guests.Value);

        if (filter.MinPrice.HasValue)
            query = query.Where(l => l.RoomTypes.Any(rt => rt.IsActive && rt.BasePricePerNight >= filter.MinPrice.Value));

        if (filter.MaxPrice.HasValue)
            query = query.Where(l => l.RoomTypes.Any(rt => rt.IsActive && rt.BasePricePerNight <= filter.MaxPrice.Value));

        if (filter.CheckIn.HasValue && filter.CheckOut.HasValue)
        {
            var checkIn = filter.CheckIn.Value;
            var checkOut = filter.CheckOut.Value;

            query = query.Where(l => l.RoomTypes.Any(rt => rt.Units.Any(u =>
                u.IsActive && !u.BookingRanges.Any(r =>
                    (r.Status == UnitRangeStatus.Reserved || r.Status == UnitRangeStatus.CheckedIn)
                    && r.CheckIn < checkOut && checkIn < r.CheckOut))));
        }

        var totalCount = await query.CountAsync(ct);

        var page = Math.Max(1, filter.Page);

        // Security audit finding (2026-09-18): SearchShortStayListingsQueryValidator now
        // rejects PageSize outside [1, 100] before this repository ever runs, but this endpoint
        // is [AllowAnonymous] -- clamping again here means a future caller that bypasses or
        // predates that validator still can't force an unbounded .Take() (matches the same
        // belt-and-suspenders pattern AdminListingService/AdminService use).
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var items = await query
            .OrderByDescending(l => l.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<ShortStayListing>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };
    }
}
