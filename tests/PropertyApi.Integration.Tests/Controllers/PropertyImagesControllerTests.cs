using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Models;
using PropertyApi.Controllers;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Integration.Tests.Controllers;

[Trait("Feature", "PropertyImages")]
public sealed class PropertyImagesControllerTests
{
    [Fact(DisplayName = "Upload persists image and uses IMediaStorageService for owner")]
    public async Task Upload_ValidOwnerImage_PersistsImage_AndCallsStorage()
    {
        await using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var property = Property.Create(
            "Nice flat",
            "A clean test property",
            ownerId,
            ListingType.ForRent);

        db.Properties.Add(property);
        await db.SaveChangesAsync();

        var storage = new FakeMediaStorageService
        {
            UploadResult = MediaUploadResult.Success(
                "https://cdn.example.com/property.jpg",
                "property-images/test-public-id")
        };

        var controller = CreateController(db, storage, ownerId);
        var files = CreateImageFiles("property.jpg", "image/jpeg", 128);

        var response = await controller.Upload(property.Id, files, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.NotNull(ok.Value);

        var savedImage = await db.PropertyImages.SingleAsync();
        Assert.Equal(property.Id, savedImage.PropertyId);
        Assert.Equal("https://cdn.example.com/property.jpg", savedImage.Url);
        Assert.Equal("property-images/test-public-id", savedImage.PublicId);
        Assert.True(savedImage.IsMain);

        Assert.Equal(1, storage.UploadCallCount);
        Assert.Equal("property-images", storage.LastUploadFolder);
        Assert.Equal("property.jpg", storage.LastUploadedFileName);
        Assert.Equal("image/jpeg", storage.LastUploadedContentType);
    }

    [Fact(DisplayName = "Upload rejects unsupported content type and does not call storage")]
    public async Task Upload_UnsupportedContentType_ReturnsBadRequest_AndDoesNotCallStorage()
    {
        await using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var property = Property.Create(
            "Nice flat",
            "A clean test property",
            ownerId,
            ListingType.ForRent);

        db.Properties.Add(property);
        await db.SaveChangesAsync();

        var storage = new FakeMediaStorageService();
        var controller = CreateController(db, storage, ownerId);
        var files = CreateImageFiles("notes.txt", "text/plain", 128);

        var response = await controller.Upload(property.Id, files, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal(0, storage.UploadCallCount);
        Assert.Empty(await db.PropertyImages.ToListAsync());
    }

    [Fact(DisplayName = "Delete removes image from database and calls IMediaStorageService delete")]
    public async Task Delete_ExistingOwnerImage_RemovesImage_AndCallsStorageDelete()
    {
        await using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var property = Property.Create(
            "Nice flat",
            "A clean test property",
            ownerId,
            ListingType.ForRent);

        var image = new PropertyImage
        {
            PropertyId = property.Id,
            Url = "https://cdn.example.com/property.jpg",
            PublicId = "property-images/test-public-id",
            IsMain = true,
            SortOrder = 0
        };

        property.Images.Add(image);
        db.Properties.Add(property);
        await db.SaveChangesAsync();

        var storage = new FakeMediaStorageService();
        var controller = CreateController(db, storage, ownerId);

        var response = await controller.Delete(property.Id, image.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(response);
        Assert.Equal(1, storage.DeleteCallCount);
        Assert.Equal("property-images/test-public-id", storage.LastDeletedPublicId);

        Assert.Empty(await db.PropertyImages.ToListAsync());

        var softDeleted = await db.PropertyImages
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == image.Id);

        Assert.True(softDeleted.IsDeleted);
        Assert.NotNull(softDeleted.DeletedAt);
    }

    [Fact(DisplayName = "Delete by non-owner returns forbidden and does not call storage")]
    public async Task Delete_NonOwner_ReturnsForbidden_AndDoesNotCallStorage()
    {
        await using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var anotherUserId = Guid.NewGuid();

        var property = Property.Create(
            "Nice flat",
            "A clean test property",
            ownerId,
            ListingType.ForRent);

        var image = new PropertyImage
        {
            PropertyId = property.Id,
            Url = "https://cdn.example.com/property.jpg",
            PublicId = "property-images/test-public-id",
            IsMain = true,
            SortOrder = 0
        };

        property.Images.Add(image);
        db.Properties.Add(property);
        await db.SaveChangesAsync();

        var storage = new FakeMediaStorageService();
        var controller = CreateController(db, storage, anotherUserId);

        var response = await controller.Delete(property.Id, image.Id, CancellationToken.None);

        Assert.IsType<ForbidResult>(response);
        Assert.Equal(0, storage.DeleteCallCount);
        Assert.Single(await db.PropertyImages.ToListAsync());
    }

    [Fact(DisplayName = "GetAll returns public property images ordered by sort order")]
    public async Task GetAll_PublicProperty_ReturnsOrderedImages()
    {
        await using var db = CreateDbContext();
        var ownerId = Guid.NewGuid();
        var property = Property.Create(
            "Nice flat",
            "A clean test property",
            ownerId,
            ListingType.ForRent);
        property.Publish();
        property.ExpiresAt = DateTime.UtcNow.AddDays(1);

        property.Images.Add(new PropertyImage
        {
            PropertyId = property.Id,
            Url = "https://cdn.example.com/2.jpg",
            PublicId = "p2",
            SortOrder = 2,
            IsMain = false
        });

        property.Images.Add(new PropertyImage
        {
            PropertyId = property.Id,
            Url = "https://cdn.example.com/1.jpg",
            PublicId = "p1",
            SortOrder = 1,
            IsMain = true
        });

        db.Properties.Add(property);
        await db.SaveChangesAsync();

        var controller = CreateController(db, new FakeMediaStorageService(), userId: null);

        var response = await controller.GetAll(property.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response);
        var images = Assert.IsAssignableFrom<IEnumerable<ImageResultDto>>(ok.Value);
        var list = images.ToList();

        Assert.Equal(2, list.Count);
        Assert.Equal("https://cdn.example.com/1.jpg", list[0].Url);
        Assert.Equal("https://cdn.example.com/2.jpg", list[1].Url);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"property-images-tests-{Guid.NewGuid():N}")
            .Options;

        return new AppDbContext(options);
    }

    private static PropertyImagesController CreateController(
        AppDbContext db,
        IMediaStorageService storage,
        Guid? userId)
    {
        var controller = new PropertyImagesController(db, storage);

        var httpContext = new DefaultHttpContext();

        if (userId.HasValue)
        {
            httpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    new[] { new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()) },
                    authenticationType: "TestAuth"));
        }

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };

        return controller;
    }

    private static IFormFileCollection CreateImageFiles(
        string fileName,
        string contentType,
        int sizeInBytes)
    {
        var content = Enumerable.Repeat((byte)1, sizeInBytes).ToArray();
        var stream = new MemoryStream(content);

        var file = new FormFile(stream, 0, stream.Length, "files", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

        var collection = new FormFileCollection { file };
        return collection;
    }

    private sealed class FakeMediaStorageService : IMediaStorageService
    {
        public MediaUploadResult UploadResult { get; init; }
            = MediaUploadResult.Success("https://cdn.example.com/default.jpg", "default-public-id");

        public int UploadCallCount { get; private set; }
        public string? LastUploadFolder { get; private set; }
        public string? LastUploadedFileName { get; private set; }
        public string? LastUploadedContentType { get; private set; }

        public int DeleteCallCount { get; private set; }
        public string? LastDeletedPublicId { get; private set; }

        public Task<MediaUploadResult> UploadImageAsync(
            Stream content,
            string fileName,
            string contentType,
            string folder,
            CancellationToken cancellationToken = default)
        {
            UploadCallCount++;
            LastUploadFolder = folder;
            LastUploadedFileName = fileName;
            LastUploadedContentType = contentType;

            return Task.FromResult(UploadResult);
        }

        public Task DeleteImageAsync(
            string publicId,
            CancellationToken cancellationToken = default)
        {
            DeleteCallCount++;
            LastDeletedPublicId = publicId;

            return Task.CompletedTask;
        }

        public Task<MediaFileResult?> GetImageAsync(
            string imageUrl,
            CancellationToken cancellationToken = default)
        {
            var result = new MediaFileResult(
                Array.Empty<byte>(),
                "image/jpeg",
                "image.jpg");

            return Task.FromResult<MediaFileResult?>(result);
        }
    }
}
