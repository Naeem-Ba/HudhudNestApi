using HudhudNestApi.Application.Common.Enums;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Models;
using HudhudNestApi.Application.Common.Services;
using HudhudNestApi.Application.Listings.Commands.DeletePropertyImage;
using HudhudNestApi.Application.Listings.Commands.UploadPropertyImages;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Auth.Tests.Application.Handlers;

public sealed class PropertyImagesHandlerTests
{
    [Fact(DisplayName = "Upload handler validates ownership and persists image through UnitOfWork")]
    public async Task Upload_OwnerImage_Persists_And_Uses_UnitOfWork()
    {
        var ownerId = Guid.NewGuid();
        var property = Property.Create("Title", "Description", ownerId, ListingType.ForRent);

        var repository = new Mock<IPropertyImageRepository>();
        repository.Setup(x => x.GetPropertyByIdAsync(property.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);
        repository.Setup(x => x.CountImagesAsync(property.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var ownership = new Mock<IPropertyOwnershipService>();
        ownership.Setup(x => x.EnsureOwnerAsync(
                property.Id,
                ownerId,
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var folderBuilder = new MediaFolderBuilder();
        var expectedFolder = folderBuilder.BuildFolder(MediaEntityType.Property, property.Id, MediaCategories.Images);

        var storage = new Mock<IMediaStorageService>();
        storage.Setup(x => x.UploadImageAsync(
                It.IsAny<Stream>(),
                "image.jpg",
                "image/jpeg",
                expectedFolder,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediaUploadResult.Success("https://cdn.test/image.jpg", "public-id"));

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new UploadPropertyImagesCommandHandler(
            repository.Object,
            ownership.Object,
            storage.Object,
            folderBuilder,
            uow.Object);

        var result = await handler.Handle(
            new UploadPropertyImagesCommand(
                property.Id,
                ownerId,
                [new UploadPropertyImageFileDto(new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]), "image.jpg", "image/jpeg", 6)]),
            CancellationToken.None);

        Assert.Equal(PropertyImageMutationStatus.Success, result.Status);
        Assert.Single(result.Images);
        repository.Verify(x => x.Add(It.Is<PropertyImage>(image => image.PropertyId == property.Id && image.IsMain)), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "P8-D06: when the provider goes down mid-upload the owner gets a failed upload, even if cleaning up the first image also fails")]
    public async Task Upload_WhenTheProviderFailsAndCleanupFailsToo_ReturnsStorageFailedInsteadOfThrowing()
    {
        var ownerId = Guid.NewGuid();
        var property = Property.Create("Title", "Description", ownerId, ListingType.ForRent);

        var repository = new Mock<IPropertyImageRepository>();
        repository.Setup(x => x.CountImagesAsync(property.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);

        var ownership = new Mock<IPropertyOwnershipService>();
        ownership.Setup(x => x.EnsureOwnerAsync(property.Id, ownerId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var storage = new Mock<IMediaStorageService>();
        storage.SetupSequence(x => x.UploadImageAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediaUploadResult.Success("https://cdn.test/1.jpg", "first"))
            .ReturnsAsync(MediaUploadResult.Failed("خدمة تخزين الصور غير متاحة حالياً."));
        storage.Setup(x => x.DeleteImageAsync("first", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("provider down"));

        var uow = new Mock<IUnitOfWork>();
        var handler = new UploadPropertyImagesCommandHandler(
            repository.Object, ownership.Object, storage.Object, new MediaFolderBuilder(), uow.Object);

        static UploadPropertyImageFileDto Jpeg() =>
            new(new MemoryStream([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]), "image.jpg", "image/jpeg", 6);

        var result = await handler.Handle(
            new UploadPropertyImagesCommand(property.Id, ownerId, [Jpeg(), Jpeg()]),
            CancellationToken.None);

        Assert.Equal(PropertyImageMutationStatus.StorageFailed, result.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "Delete handler refuses non-owner before deleting from storage")]
    public async Task Delete_NonOwner_ReturnsForbidden_Without_DeletingStorage()
    {
        var ownerId = Guid.NewGuid();
        var property = Property.Create("Title", "Description", ownerId, ListingType.ForRent);
        var image = new PropertyImage { PropertyId = property.Id, Url = "url", PublicId = "public-id", IsMain = true };
        property.Images.Add(image);

        var repository = new Mock<IPropertyImageRepository>();
        repository.Setup(x => x.GetPropertyWithImagesAsync(property.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);

        var ownership = new Mock<IPropertyOwnershipService>();
        ownership.Setup(x => x.EnsureOwnerAsync(
                property.Id,
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HudhudNestApi.Application.Common.Exceptions.ForbiddenException("Forbidden"));

        var storage = new Mock<IMediaStorageService>();
        var uow = new Mock<IUnitOfWork>();
        var handler = new DeletePropertyImageCommandHandler(
            repository.Object,
            ownership.Object,
            storage.Object,
            uow.Object);

        var result = await handler.Handle(
            new DeletePropertyImageCommand(property.Id, image.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(PropertyImageMutationStatus.Forbidden, result.Status);
        repository.Verify(x => x.Remove(It.IsAny<PropertyImage>()), Times.Never);
        storage.Verify(x => x.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // STG-001: the removed main image kept IsMain=true (soft delete) while the replacement was
    // promoted in the same SaveChanges, so the one-main-per-property unique index rejected it (HTTP 500).
    [Fact(DisplayName = "Delete main image clears its IsMain, saves, then promotes the next image")]
    public async Task Delete_MainImage_ClearsFlagBeforePromotingReplacement()
    {
        var ownerId = Guid.NewGuid();
        var property = Property.Create("Title", "Description", ownerId, ListingType.ForRent);
        var main = new PropertyImage { PropertyId = property.Id, Url = "u1", PublicId = "p1", IsMain = true, SortOrder = 0 };
        var next = new PropertyImage { PropertyId = property.Id, Url = "u2", PublicId = "p2", IsMain = false, SortOrder = 1 };
        property.Images.Add(main);
        property.Images.Add(next);

        var repository = new Mock<IPropertyImageRepository>();
        repository.Setup(x => x.GetPropertyWithImagesAsync(property.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);
        var ownership = new Mock<IPropertyOwnershipService>();
        var storage = new Mock<IMediaStorageService>();

        var saveSnapshots = new List<(bool MainFlag, bool NextFlag)>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => saveSnapshots.Add((main.IsMain, next.IsMain)))
            .ReturnsAsync(1);

        var handler = new DeletePropertyImageCommandHandler(
            repository.Object, ownership.Object, storage.Object, uow.Object);

        var result = await handler.Handle(
            new DeletePropertyImageCommand(property.Id, main.Id, ownerId),
            CancellationToken.None);

        Assert.Equal(PropertyImageMutationStatus.Success, result.Status);
        Assert.Equal(2, saveSnapshots.Count);
        Assert.Equal((false, false), saveSnapshots[0]); // first save: nobody is main yet
        Assert.Equal((false, true), saveSnapshots[1]);  // second save: replacement promoted
        repository.Verify(x => x.Remove(main), Times.Once);
        storage.Verify(x => x.DeleteImageAsync("p1", It.IsAny<CancellationToken>()), Times.Once);
    }

    // STG-002: deleting the only image must leave no soft-deleted row holding IsMain.
    [Fact(DisplayName = "Delete the only image clears its IsMain so the next upload can become main")]
    public async Task Delete_OnlyImage_ClearsMainFlag()
    {
        var ownerId = Guid.NewGuid();
        var property = Property.Create("Title", "Description", ownerId, ListingType.ForRent);
        var only = new PropertyImage { PropertyId = property.Id, Url = "u1", PublicId = "p1", IsMain = true };
        property.Images.Add(only);

        var repository = new Mock<IPropertyImageRepository>();
        repository.Setup(x => x.GetPropertyWithImagesAsync(property.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = new DeletePropertyImageCommandHandler(
            repository.Object, new Mock<IPropertyOwnershipService>().Object,
            new Mock<IMediaStorageService>().Object, uow.Object);

        var result = await handler.Handle(
            new DeletePropertyImageCommand(property.Id, only.Id, ownerId), CancellationToken.None);

        Assert.Equal(PropertyImageMutationStatus.Success, result.Status);
        Assert.False(only.IsMain);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
