using PropertyApi.Application.Common.Enums;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Models;
using PropertyApi.Application.Common.Services;
using PropertyApi.Application.Listings.Commands.DeletePropertyImage;
using PropertyApi.Application.Listings.Commands.UploadPropertyImages;
using PropertyApi.Application.Listings.DTOs;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Auth.Tests.Application.Handlers;

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
            .ThrowsAsync(new PropertyApi.Application.Common.Exceptions.ForbiddenException("Forbidden"));

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
}
