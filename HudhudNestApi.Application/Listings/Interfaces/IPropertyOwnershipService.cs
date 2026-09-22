using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Listings.Interfaces;

public interface IPropertyOwnershipService
{
    Task<Property> GetOwnedPropertyOrThrowAsync(
        Guid propertyId,
        Guid userId,
        string operation,
        CancellationToken ct = default);

    Task EnsureOwnerAsync(
        Guid propertyId,
        Guid userId,
        string operation,
        CancellationToken ct = default);
}
