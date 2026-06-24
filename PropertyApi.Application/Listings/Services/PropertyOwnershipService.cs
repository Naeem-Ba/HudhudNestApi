using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Listings.Services;

public sealed class PropertyOwnershipService : IPropertyOwnershipService
{
    private readonly IPropertyRepository _properties;

    public PropertyOwnershipService(IPropertyRepository properties)
    {
        _properties = properties;
    }

    public async Task<Property> GetOwnedPropertyOrThrowAsync(
        Guid propertyId,
        Guid userId,
        string operation,
        CancellationToken ct = default)
    {
        if (propertyId == Guid.Empty)
            throw new ArgumentException("PropertyId is required.", nameof(propertyId));

        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));

        var property = await _properties.GetByIdAsync(propertyId, ct);
        if (property is null)
            throw new NotFoundException("Property was not found.");

        if (property.OwnerId != userId)
        {
            var normalizedOperation = string.IsNullOrWhiteSpace(operation)
                ? "access"
                : operation.Trim();

            throw new ForbiddenException(
                $"The current user is not allowed to {normalizedOperation} this property.");
        }

        return property;
    }

    public async Task EnsureOwnerAsync(
        Guid propertyId,
        Guid userId,
        string operation,
        CancellationToken ct = default)
    {
        _ = await GetOwnedPropertyOrThrowAsync(propertyId, userId, operation, ct);
    }
}
