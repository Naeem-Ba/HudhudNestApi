using Microsoft.EntityFrameworkCore;
using WohnungenApi.Infrastructure.Persistence;
using WohnungenApi.Application.Properties.DTOs;
using WohnungenApi.Domain.Listings.Entities;
using WohnungenApi.Application.Listings.Interfaces;

namespace WohnungenApi.Infrastructure.Repositories;

public sealed class PropertyRepository : IPropertyRepository
{
    private readonly AppDbContext _db;

    public PropertyRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(Property property, CancellationToken ct = default)
    {
        await _db.Properties.AddAsync(property, ct);
    }

    public async Task<Property?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Properties
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<Property?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Properties
            .Include(p => p.Owner)
            .Include(p => p.Images)
            .Include(p => p.PropertyAmenities)
                .ThenInclude(pa => pa.Amenity)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<PagedResult<Property>> GetPagedAsync(
        PropertyFilterDto filter,
        CancellationToken ct = default)
    {
        var query = _db.Properties
            .Include(p => p.Owner)
            .Include(p => p.Images.Where(i => i.IsMain))
            .AsQueryable();

        // ── Filters ───────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(filter.CountryCode))
            query = query.Where(p => p.CountryCode == filter.CountryCode.ToUpperInvariant());

        if (!string.IsNullOrWhiteSpace(filter.City))
            query = query.Where(p => EF.Functions.ILike(p.City, $"%{filter.City}%"));

        if (!string.IsNullOrWhiteSpace(filter.Region))
            query = query.Where(p => EF.Functions.ILike(p.Region!, $"%{filter.Region}%"));

        if (filter.ListingType.HasValue)
            query = query.Where(p => p.ListingType == filter.ListingType.Value);

        if (filter.Status.HasValue)
            query = query.Where(p => p.Status == filter.Status.Value);

        if (filter.Condition.HasValue)
            query = query.Where(p => p.Condition == filter.Condition.Value);

        if (filter.MinPrice.HasValue)
            query = query.Where(p =>
                p.ColdRent >= filter.MinPrice ||
                p.WarmRent >= filter.MinPrice ||
                p.PurchasePrice >= filter.MinPrice);

        if (filter.MaxPrice.HasValue)
            query = query.Where(p =>
                p.ColdRent <= filter.MaxPrice ||
                p.WarmRent <= filter.MaxPrice ||
                p.PurchasePrice <= filter.MaxPrice);

        if (filter.MinRooms.HasValue)
            query = query.Where(p => p.Rooms >= filter.MinRooms);

        if (filter.MaxRooms.HasValue)
            query = query.Where(p => p.Rooms <= filter.MaxRooms);

        if (filter.MinArea.HasValue)
            query = query.Where(p => p.Area >= filter.MinArea);

        if (filter.MaxArea.HasValue)
            query = query.Where(p => p.Area <= filter.MaxArea);

        if (filter.HasBalcony.HasValue)
            query = query.Where(p => p.HasBalcony == filter.HasBalcony);

        if (filter.HasElevator.HasValue)
            query = query.Where(p => p.HasElevator == filter.HasElevator);

        if (filter.HasParkingSpace.HasValue)
            query = query.Where(p => p.HasParkingSpace == filter.HasParkingSpace);

        if (filter.OwnerId.HasValue)
            query = query.Where(p => p.OwnerId == filter.OwnerId);

        if (filter.AmenityIds is { Count: > 0 })
            query = query.Where(p =>
                filter.AmenityIds.All(aid =>
                    p.PropertyAmenities.Any(pa => pa.AmenityId == aid)));

        // Only published listings by default
        query = query.Where(p => p.IsPublished);

        // ── Count (before pagination) ─────────────────────────
        var totalCount = await query.CountAsync(ct);

        // ── Sort ─────────────────────────────────────────────
        query = (filter.SortBy?.ToLower(), filter.SortDescending) switch
        {
            ("createdat", true) => query.OrderByDescending(p => p.CreatedAt),
            ("createdat", false) => query.OrderBy(p => p.CreatedAt),
            ("purchaseprice", true) => query.OrderByDescending(p => p.PurchasePrice),
            ("purchaseprice", false) => query.OrderBy(p => p.PurchasePrice),
            ("coldrent", true) => query.OrderByDescending(p => p.ColdRent),
            ("coldrent", false) => query.OrderBy(p => p.ColdRent),
            ("area", true) => query.OrderByDescending(p => p.Area),
            ("area", false) => query.OrderBy(p => p.Area),
            _ => query.OrderByDescending(p => p.CreatedAt)
        };

        // ── Paginate ─────────────────────────────────────────
        var pageSize = Math.Min(filter.PageSize, 100); // hard cap at 100
        var items = await query
            .Skip((filter.Page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Property>
        {
            Items = items,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = pageSize
        };
    }

    public async Task<IReadOnlyList<Property>> GetByOwnerAsync(
        Guid ownerId,
        CancellationToken ct = default)
    {
        return await _db.Properties
            .Where(p => p.OwnerId == ownerId)
            .Include(p => p.Images.Where(i => i.IsMain))
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
    {
        return await _db.Properties.AnyAsync(p => p.Id == id, ct);
    }

    public void Update(Property property)
    {
        _db.Properties.Update(property);
    }

    public void Remove(Property property)
    {
        // Soft delete is handled in AppDbContext.SaveChangesAsync()
        // Calling Remove() here will be intercepted and converted to IsDeleted=true
        _db.Properties.Remove(property);
    }
}