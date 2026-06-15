using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Listings.Interfaces;

public interface IPropertyImageRepository
{
    Task<Property?> GetPropertyByIdAsync(Guid propertyId, CancellationToken ct = default);
    Task<Property?> GetPropertyWithImagesAsync(Guid propertyId, CancellationToken ct = default);
    Task<int> CountImagesAsync(Guid propertyId, CancellationToken ct = default);
    Task<bool> PublicPropertyExistsAsync(Guid propertyId, DateTime utcNow, CancellationToken ct = default);
    Task<IReadOnlyList<PropertyImageDto>> GetImagesAsync(Guid propertyId, CancellationToken ct = default);
    void Add(PropertyImage image);
    void Remove(PropertyImage image);
}

