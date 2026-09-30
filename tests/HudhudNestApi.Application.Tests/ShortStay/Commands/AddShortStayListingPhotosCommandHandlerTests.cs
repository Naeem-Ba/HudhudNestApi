using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Models;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.ShortStay.Commands.AddShortStayListingPhotos;
using HudhudNestApi.Application.ShortStay.DTOs;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Application.Tests.ShortStay.Commands;

public sealed class AddShortStayListingPhotosCommandHandlerTests
{
    // توقيع PNG صالح (8 بايت) بحشو حتى 12 بايت لتمرير فحص التوقيع.
    private static readonly byte[] ValidPngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private static ShortStayListing CreateListing(Guid ownerId) =>
        ShortStayListing.Create(ownerId, 1, "شاليه", "desc", 4, 2, 1,
            new TimeOnly(14, 0), new TimeOnly(11, 0), 33.5m, 36.3m);

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

    private static AddShortStayListingPhotosCommandHandler HandlerFor(
        ShortStayListing listing, IMediaStorageService? storage = null, NoOpUnitOfWork? uow = null) =>
        new(new StubRepository(listing), storage ?? new StubStorage(), uow ?? new NoOpUnitOfWork());

    private static Task<IReadOnlyList<string>> Upload(
        AddShortStayListingPhotosCommandHandler handler, ShortStayListing listing, Guid owner,
        params UploadPropertyImageFileDto[] files) =>
        handler.Handle(new AddShortStayListingPhotosCommand(listing.Id, owner, files), CancellationToken.None);

    [Fact]
    public async Task Handle_Throws_NotFound_WhenListingMissing()
    {
        var owner = Guid.NewGuid();
        var handler = HandlerFor(CreateListing(owner));

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new AddShortStayListingPhotosCommand(Guid.NewGuid(), owner, [ValidFile()]), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_Throws_WhenMoreThanTenFilesInOneRequest()
    {
        var owner = Guid.NewGuid();
        var listing = CreateListing(owner);
        var files = Enumerable.Range(0, 11).Select(i => ValidFile($"p{i}.png")).ToArray();

        await Assert.ThrowsAsync<DomainException>(() => Upload(HandlerFor(listing), listing, owner, files));
    }

    [Fact]
    public async Task Handle_Throws_WhenListingWouldExceedTwentyPhotos()
    {
        var owner = Guid.NewGuid();
        var listing = CreateListing(owner);
        for (var i = 0; i < 20; i++)
            listing.Photos.Add(new ShortStayListingPhoto { Url = $"u{i}", PublicId = $"p{i}", SortOrder = i, ShortStayListingId = listing.Id });

        await Assert.ThrowsAsync<DomainException>(() => Upload(HandlerFor(listing), listing, owner, ValidFile()));
        Assert.Equal(20, listing.Photos.Count);
    }

    [Theory]
    [InlineData("image/gif", "photo.png")]   // MIME not allowed
    [InlineData("image/png", "photo.gif")]   // extension not allowed
    [InlineData("image/png", "photo")]       // no extension
    public async Task Handle_Rejects_UnsupportedTypeOrExtension(string contentType, string fileName)
    {
        var owner = Guid.NewGuid();
        var listing = CreateListing(owner);
        var file = new UploadPropertyImageFileDto(new MemoryStream(ValidPngBytes), fileName, contentType, ValidPngBytes.Length);

        await Assert.ThrowsAsync<DomainException>(() => Upload(HandlerFor(listing), listing, owner, file));
        Assert.Empty(listing.Photos);
    }

    [Fact]
    public async Task Handle_Rejects_EmptyFile()
    {
        var owner = Guid.NewGuid();
        var listing = CreateListing(owner);
        var empty = new UploadPropertyImageFileDto(new MemoryStream([]), "e.png", "image/png", 0);

        await Assert.ThrowsAsync<DomainException>(() => Upload(HandlerFor(listing), listing, owner, empty));
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    public async Task Handle_Rejects_FileWhoseBytesDoNotMatchDeclaredType(string contentType)
    {
        var owner = Guid.NewGuid();
        var listing = CreateListing(owner);
        var notAnImage = "<script>alert(1)</script>"u8.ToArray();
        var ext = contentType == "image/jpeg" ? ".jpg" : contentType == "image/png" ? ".png" : ".webp";
        var file = new UploadPropertyImageFileDto(new MemoryStream(notAnImage), "x" + ext, contentType, notAnImage.Length);

        await Assert.ThrowsAsync<DomainException>(() => Upload(HandlerFor(listing), listing, owner, file));
        Assert.Empty(listing.Photos);
    }

    [Fact]
    public async Task Handle_Rejects_NonSeekableStream()
    {
        var owner = Guid.NewGuid();
        var listing = CreateListing(owner);
        var file = new UploadPropertyImageFileDto(new NonSeekableStream(ValidPngBytes), "p.png", "image/png", ValidPngBytes.Length);

        await Assert.ThrowsAsync<DomainException>(() => Upload(HandlerFor(listing), listing, owner, file));
    }

    [Fact]
    public async Task Handle_Accepts_JpegAndWebp_AndOnlyTheFirstPhotoIsMain()
    {
        var owner = Guid.NewGuid();
        var listing = CreateListing(owner);
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0];
        byte[] webp = [0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50];

        var urls = await Upload(HandlerFor(listing), listing, owner,
            new UploadPropertyImageFileDto(new MemoryStream(jpeg), "a.jpg", "image/jpeg", jpeg.Length),
            new UploadPropertyImageFileDto(new MemoryStream(webp), "b.webp", "image/webp", webp.Length));

        Assert.Equal(2, urls.Count);
        var photos = listing.Photos.OrderBy(p => p.SortOrder).ToList();
        Assert.True(photos[0].IsMain);
        Assert.False(photos[1].IsMain);
        Assert.Equal([0, 1], photos.Select(p => p.SortOrder));
    }

    [Fact]
    public async Task Handle_DeletesAlreadyUploadedFiles_WhenALaterUploadFails()
    {
        var owner = Guid.NewGuid();
        var listing = CreateListing(owner);
        var storage = new RecordingStorage(failOnCall: 2);

        await Assert.ThrowsAsync<DomainException>(() =>
            Upload(HandlerFor(listing, storage), listing, owner, ValidFile("1.png"), ValidFile("2.png")));

        Assert.Single(storage.Uploaded);
        Assert.Equal(storage.Uploaded, storage.Deleted);
    }

    [Fact]
    public async Task Handle_DeletesUploadedFiles_WhenSavingTheListingFails()
    {
        var owner = Guid.NewGuid();
        var listing = CreateListing(owner);
        var storage = new RecordingStorage();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Upload(HandlerFor(listing, storage, new NoOpUnitOfWork { ThrowOnSave = true }), listing, owner, ValidFile()));

        Assert.Single(storage.Uploaded);
        Assert.Equal(storage.Uploaded, storage.Deleted);
    }

    private sealed class RecordingStorage(int failOnCall = 0) : IMediaStorageService
    {
        private int _calls;
        public List<string> Uploaded { get; } = [];
        public List<string> Deleted { get; } = [];

        public Task<MediaUploadResult> UploadImageAsync(Stream content, string fileName, string contentType, string folder, CancellationToken ct = default)
        {
            if (++_calls == failOnCall) return Task.FromResult(MediaUploadResult.Failed("storage down"));
            var publicId = Guid.NewGuid().ToString();
            Uploaded.Add(publicId);
            return Task.FromResult(MediaUploadResult.Success($"https://cdn.test/{fileName}", publicId));
        }

        public Task DeleteImageAsync(string publicId, CancellationToken ct = default)
        {
            Deleted.Add(publicId);
            return Task.CompletedTask;
        }

        public Task<MediaFileResult?> GetImageAsync(string imageUrl, CancellationToken ct = default) => Task.FromResult<MediaFileResult?>(null);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
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
        public Task<int> CountActiveByOwnerAsync(Guid ownerId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> CountActiveByOwnerIdsAsync(IReadOnlyCollection<Guid> ownerIds, CancellationToken ct = default) => throw new NotImplementedException();
        public void Update(ShortStayListing listing) { }
        public void Remove(ShortStayListing listing) { }
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public bool ThrowOnSave { get; init; }
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
            => ThrowOnSave ? throw new InvalidOperationException("save failed") : Task.FromResult(1);
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
