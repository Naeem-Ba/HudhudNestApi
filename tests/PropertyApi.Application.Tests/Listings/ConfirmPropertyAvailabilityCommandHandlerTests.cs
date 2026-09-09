using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Commands.ConfirmPropertyAvailability;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Tests.Listings;

public sealed class ConfirmPropertyAvailabilityCommandHandlerTests
{
    [Fact]
    public async Task Handle_SetsLastConfirmedAvailableAt_ForOwner()
    {
        var owner = Guid.NewGuid();
        var property = CreateProperty(owner);
        var repo = new StubPropertyRepository(property);
        var handler = new ConfirmPropertyAvailabilityCommandHandler(repo, new NoOpUnitOfWork());

        await handler.Handle(
            new ConfirmPropertyAvailabilityCommand(property.Id, owner, IsAdmin: false),
            CancellationToken.None);

        Assert.NotNull(property.LastConfirmedAvailableAt);
    }

    [Fact]
    public async Task Handle_Throws_WhenRequestingUserIsNotOwnerOrAdmin()
    {
        var owner = Guid.NewGuid();
        var strangerId = Guid.NewGuid();
        var property = CreateProperty(owner);
        var repo = new StubPropertyRepository(property);
        var handler = new ConfirmPropertyAvailabilityCommandHandler(repo, new NoOpUnitOfWork());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new ConfirmPropertyAvailabilityCommand(property.Id, strangerId, IsAdmin: false),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Throws_WhenPropertyDoesNotExist()
    {
        var repo = new StubPropertyRepository(property: null);
        var handler = new ConfirmPropertyAvailabilityCommandHandler(repo, new NoOpUnitOfWork());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new ConfirmPropertyAvailabilityCommand(Guid.NewGuid(), Guid.NewGuid(), IsAdmin: false),
            CancellationToken.None));
    }

    private static Property CreateProperty(Guid ownerId) =>
        Property.Create(
            title: "Test apartment",
            description: "For availability-confirmation tests",
            ownerId: ownerId,
            listingType: ListingType.ForRent,
            countryCode: "SY",
            currencyCode: "SYP");

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task AcquireAdvisoryLockAsync(long key, CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose()
        {
        }
    }

    private sealed class StubPropertyRepository : IPropertyRepository
    {
        private readonly Property? _property;

        public StubPropertyRepository(Property? property)
        {
            _property = property;
        }

        public Task AddAsync(Property property, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<Property?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_property is not null && _property.Id == id ? _property : null);

        public Task<Property?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<Property?> GetPublishedByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<PagedResult<Property>> GetPagedAsync(PropertyFilterDto filter, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<Property>> GetByOwnerAsync(Guid ownerId, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<bool> IsPubliclyVisibleAsync(Guid id, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<(int SoldCount, int RentedCount)> GetDealCountsByOwnerAsync(
            Guid ownerId,
            CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<int> CountActiveListingsByOwnerAsync(
            Guid ownerId,
            CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<int> CountActiveListingsByAgencyAsync(
            Guid agencyId,
            CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<Property>> FindPotentialDuplicatesAsync(
            int neighborhoodId,
            ListingType listingType,
            decimal? price,
            decimal? area,
            decimal maxTolerancePercent,
            CancellationToken ct = default)
            => throw new NotImplementedException();

        public void Update(Property property)
        {
        }

        public void Remove(Property property)
        {
        }
    }
}
