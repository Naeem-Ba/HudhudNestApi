using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.ShortStay.Commands.SetShortStayListingAmenities;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Application.Tests.ShortStay.Commands;

public sealed class SetShortStayListingAmenitiesCommandHandlerTests
{
    private static ShortStayListing CreateListing(Guid ownerId) =>
        ShortStayListing.Create(ownerId, 1, "شاليه", "desc", 4, 2, 1,
            new TimeOnly(14, 0), new TimeOnly(11, 0), 33.5m, 36.3m);

    [Fact]
    public async Task Handle_SetsAmenities_ForOwner()
    {
        var owner = Guid.NewGuid();
        var listing = CreateListing(owner);
        var repo = new StubRepository(listing);
        var handler = new SetShortStayListingAmenitiesCommandHandler(repo, new NoOpUnitOfWork());

        var amenityId = Guid.NewGuid();
        var result = await handler.Handle(
            new SetShortStayListingAmenitiesCommand(listing.Id, owner, [amenityId]), CancellationToken.None);

        Assert.True(result);
        Assert.Single(listing.ListingAmenities);
        Assert.Equal(amenityId, listing.ListingAmenities.Single().AmenityId);
    }

    [Fact]
    public async Task Handle_Throws_WhenNotOwner()
    {
        var owner = Guid.NewGuid();
        var listing = CreateListing(owner);
        var repo = new StubRepository(listing);
        var handler = new SetShortStayListingAmenitiesCommandHandler(repo, new NoOpUnitOfWork());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new SetShortStayListingAmenitiesCommand(listing.Id, Guid.NewGuid(), [Guid.NewGuid()]),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Throws_WhenListingNotFound()
    {
        var repo = new StubRepository(null);
        var handler = new SetShortStayListingAmenitiesCommandHandler(repo, new NoOpUnitOfWork());

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new SetShortStayListingAmenitiesCommand(Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()]),
            CancellationToken.None));
    }

    private sealed class StubRepository : IShortStayListingRepository
    {
        private readonly ShortStayListing? _listing;
        public StubRepository(ShortStayListing? listing) => _listing = listing;

        public Task AddAsync(ShortStayListing listing, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<ShortStayListing>> SearchAsync(ShortStayListingSearchFilter filter, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ShortStayListing?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(_listing != null && id == _listing.Id ? _listing : null);
        public Task<ShortStayListing?> GetPublishedByIdWithDetailsAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ShortStayListing>> GetByOwnerAsync(Guid ownerId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountActiveByOwnerAsync(Guid ownerId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountActiveByOwnerIdsAsync(IReadOnlyCollection<Guid> ownerIds, CancellationToken ct = default) => throw new NotImplementedException();
        public void Update(ShortStayListing listing) { }
        public void Remove(ShortStayListing listing) { }
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task AcquireAdvisoryLockAsync(long key, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> TrySaveChangesAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<IReadOnlyList<object>> SaveChangesDroppingConcurrencyConflictsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
        public void Dispose() { }
    }
}
