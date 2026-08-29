using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Models;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.ShortStay.Commands.AddShortStayListingPhotos;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Application.Tests.ShortStay.Commands;

public sealed class AddShortStayListingPhotosCommandHandlerTests
{
    // توقيع PNG صالح (8 بايت) بحشو حتى 12 بايت لتمرير فحص التوقيع.
    private static readonly byte[] ValidPngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private static ShortStayListing CreateListing(Guid ownerId) =>
        ShortStayListing.Create(ownerId, 1, "شاليه", "desc", 4, 2, 1,
            new TimeOnly(14, 0), new TimeOnly(11, 0), 0m, 0m);

    private static UploadPropertyImageFileDto ValidFile(string name = "photo.png") =>
        new(new MemoryStream(ValidPngBytes), name, "image/png", ValidPngBytes.Length);

    [Fact]
    public async Task Handle_UploadsPhoto_ForOwner()
    {
        var owner = Guid.NewGuid();
        var listing = CreateListing(owner);
        var handler = new AddShortStayListingPhotosCommandHandler(
            new StubRepository(listing), new StubStorage(), new NoOpUnitOfWork());

        var result = await handler.Handle(
            new AddShortStayListingPhotosCommand(listing.Id, owner, [ValidFile()]), CancellationToken.None);

        Assert.Single(result);
        Assert.Single(listing.Photos);
        Assert.True(listing.Photos.First().IsMain);
    }

    [Fact]
    public async Task Handle_Throws_WhenNotOwner()
    {
        var owner = Guid.NewGuid();
        var listing = CreateListing(owner);
        var handler = new AddShortStayListingPhotosCommandHandler(
            new StubRepository(listing), new StubStorage(), new NoOpUnitOfWork());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new AddShortStayListingPhotosCommand(listing.Id, Guid.NewGuid(), [ValidFile()]), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Throws_WhenFileTooLarge()
    {
        var owner = Guid.NewGuid();
        var listing = CreateListing(owner);
        var handler = new AddShortStayListingPhotosCommandHandler(
            new StubRepository(listing), new StubStorage(), new NoOpUnitOfWork());

        var oversized = new UploadPropertyImageFileDto(new MemoryStream(ValidPngBytes), "big.png", "image/png", 10_000_000);

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new AddShortStayListingPhotosCommand(listing.Id, owner, [oversized]), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Throws_WhenNoFiles()
    {
        var owner = Guid.NewGuid();
        var listing = CreateListing(owner);
        var handler = new AddShortStayListingPhotosCommandHandler(
            new StubRepository(listing), new StubStorage(), new NoOpUnitOfWork());

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(
            new AddShortStayListingPhotosCommand(listing.Id, owner, []), CancellationToken.None));
    }

    private sealed class StubStorage : IMediaStorageService
    {
        public Task<MediaUploadResult> UploadImageAsync(Stream content, string fileName, string contentType, string folder, CancellationToken ct = default)
            => Task.FromResult(MediaUploadResult.Success($"https://cdn.test/{fileName}", Guid.NewGuid().ToString()));
        public Task DeleteImageAsync(string publicId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<MediaFileResult?> GetImageAsync(string imageUrl, CancellationToken ct = default) => Task.FromResult<MediaFileResult?>(null);
    }

    private sealed class StubRepository : IShortStayListingRepository
    {
        private readonly ShortStayListing _listing;
        public StubRepository(ShortStayListing listing) => _listing = listing;

        public Task AddAsync(ShortStayListing listing, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PagedResult<ShortStayListing>> SearchAsync(ShortStayListingSearchFilter filter, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ShortStayListing?> GetByIdWithDetailsAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(id == _listing.Id ? _listing : null);
        public Task<ShortStayListing?> GetPublishedByIdWithDetailsAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ShortStayListing>> GetByOwnerAsync(Guid ownerId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public void Update(ShortStayListing listing) { }
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task BeginTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task CommitTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task AcquireAdvisoryLockAsync(long key, CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
