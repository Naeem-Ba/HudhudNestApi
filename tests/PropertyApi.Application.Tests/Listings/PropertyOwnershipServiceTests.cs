using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Listings.Services;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Tests.Listings;

public sealed class PropertyOwnershipServiceTests
{
    [Fact]
    public async Task UserA_CannotUpdatePropertyOwnedByUserB()
    {
        var ownerB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var property = Property.Create(
            title: "Test apartment",
            description: "Owned by user B",
            ownerId: ownerB,
            listingType: ListingType.ForRent,
            countryCode: "SY",
            currencyCode: "SYP");

        var service = new PropertyOwnershipService(
            new StubPropertyRepository(property));

        var exception = await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.GetOwnedPropertyOrThrowAsync(
                property.Id,
                userA,
                operation: "update",
                ct: CancellationToken.None));

        Assert.Contains("not allowed to update", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Owner_CanUpdateOwnProperty()
    {
        var owner = Guid.NewGuid();
        var property = Property.Create(
            title: "Test apartment",
            description: "Owned by current user",
            ownerId: owner,
            listingType: ListingType.ForSale,
            countryCode: "SY",
            currencyCode: "SYP");

        var service = new PropertyOwnershipService(
            new StubPropertyRepository(property));

        var result = await service.GetOwnedPropertyOrThrowAsync(
            property.Id,
            owner,
            operation: "update",
            ct: CancellationToken.None);

        Assert.Same(property, result);
    }

    private sealed class StubPropertyRepository : IPropertyRepository
    {
        private readonly Property _property;

        public StubPropertyRepository(Property property)
        {
            _property = property;
        }

        public Task AddAsync(Property property, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<Property?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(id == _property.Id ? _property : null);

        public Task<Property?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(id == _property.Id ? _property : null);

        public Task<Property?> GetPublishedByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(id == _property.Id ? _property : null);

        public Task<PagedResult<Property>> GetPagedAsync(PropertyFilterDto filter, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<Property>> GetByOwnerAsync(Guid ownerId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Property>>(
                _property.OwnerId == ownerId
                    ? new[] { _property }
                    : Array.Empty<Property>());

        public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(id == _property.Id);

        public void Update(Property property)
        {
        }

        public void Remove(Property property)
        {
        }
    }
}
