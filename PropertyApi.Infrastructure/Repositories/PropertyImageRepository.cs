using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories;

public sealed class PropertyImageRepository : IPropertyImageRepository
{
    private readonly AppDbContext _db;

    public PropertyImageRepository(AppDbContext db)
        => _db = db;

    public Task<Property?> GetPropertyByIdAsync(Guid propertyId, CancellationToken ct = default)
    {
        return _db.Properties
            .AsNoTracking()
            .FirstOrDefaultAsync(property => property.Id == propertyId, ct);
    }

    public Task<Property?> GetPropertyWithImagesAsync(Guid propertyId, CancellationToken ct = default)
    {
        return _db.Properties
            .Include(property => property.Images)
            .FirstOrDefaultAsync(property => property.Id == propertyId, ct);
    }

    public Task<int> CountImagesAsync(Guid propertyId, CancellationToken ct = default)
    {
        return _db.PropertyImages
            .CountAsync(image => image.PropertyId == propertyId, ct);
    }

    public Task<bool> PublicPropertyExistsAsync(
        Guid propertyId,
        DateTime utcNow,
        CancellationToken ct = default)
    {
        return _db.Properties
            .AsNoTracking()
            .AnyAsync(
                property =>
                    property.Id == propertyId &&
                    property.IsPublished &&
                    (property.ExpiresAt == null || property.ExpiresAt > utcNow),
                ct);
    }

    public async Task<IReadOnlyList<PropertyImageDto>> GetImagesAsync(
        Guid propertyId,
        CancellationToken ct = default)
    {
        return await _db.PropertyImages
            .AsNoTracking()
            .Where(image => image.PropertyId == propertyId)
            .OrderBy(image => image.SortOrder)
            .Select(image => new PropertyImageDto(
                image.Id,
                image.Url,
                image.IsMain,
                image.SortOrder))
            .ToListAsync(ct);
    }

    public void Add(PropertyImage image)
    {
        _db.PropertyImages.Add(image);
    }

    public void Remove(PropertyImage image)
    {
        _db.PropertyImages.Remove(image);
    }
}
