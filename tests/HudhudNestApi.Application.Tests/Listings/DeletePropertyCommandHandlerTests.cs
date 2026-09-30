using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Commands.DeleteProperty;
using HudhudNestApi.Application.Listings.Events;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using Xunit;

namespace HudhudNestApi.Application.Tests.Listings;

/// <summary>
/// Covers the privacy fix in docs/privacy/privacy-gaps.md (P1): deleting a listing must
/// also remove its images from Cloudinary — previously nothing in this codebase ever
/// did, so every deleted/expired listing's photos were orphaned in cloud storage forever.
/// </summary>
public sealed class DeletePropertyCommandHandlerTests
{
    [Fact]
    public async Task Handle_DeletesEachImageFromStorage_ByPublicId()
    {
        // Arrange
        var ownerId = Guid.NewGuid();
        var property = CreateProperty(ownerId);

        var images = new List<PropertyImage>
        {
            CreateImage(property.Id, "properties/abc123"),
            CreateImage(property.Id, "properties/def456")
        };
        property.Images = images;

        var ownership = new Mock<IPropertyOwnershipService>();
        ownership
            .Setup(x => x.GetOwnedPropertyOrThrowAsync(
                property.Id,
                ownerId,
                "delete",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);

        var repo = new Mock<IPropertyRepository>();

        var imageRepo = new Mock<IPropertyImageRepository>();
        imageRepo
            .Setup(x => x.GetPropertyWithImagesAsync(property.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);

        var storage = new Mock<IMediaStorageService>();

        var uow = new Mock<IUnitOfWork>();
        var auditLogs = new Mock<IAuditLogService>();
        var publisher = new Mock<IPublisher>();
        var logger = new Mock<ILogger<DeletePropertyCommandHandler>>();

        var sut = new DeletePropertyCommandHandler(
            repo.Object,
            imageRepo.Object,
            ownership.Object,
            storage.Object,
            uow.Object,
            auditLogs.Object,
            publisher.Object,
            logger.Object);

        // Act
        var result = await sut.Handle(
            new DeletePropertyCommand(property.Id, ownerId, "127.0.0.1"),
            CancellationToken.None);

        // Assert
        Assert.True(result);

        storage.Verify(
            x => x.DeleteImageAsync("properties/abc123", It.IsAny<CancellationToken>()),
            Times.Once);

        storage.Verify(
            x => x.DeleteImageAsync("properties/def456", It.IsAny<CancellationToken>()),
            Times.Once);

        repo.Verify(x => x.Remove(property), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        auditLogs.Verify(
            x => x.LogAsync(
                ownerId,
                AuditActions.DeleteProperty,
                "127.0.0.1",
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_SkipsImagesWithNoPublicId()
    {
        var ownerId = Guid.NewGuid();
        var property = CreateProperty(ownerId);
        property.Images = new List<PropertyImage>
        {
            CreateImage(property.Id, publicId: string.Empty)
        };

        var (sut, storage, _, _) = BuildSut(property, ownerId);

        await sut.Handle(
            new DeletePropertyCommand(property.Id, ownerId, "127.0.0.1"),
            CancellationToken.None);

        storage.Verify(
            x => x.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_StillSucceeds_WhenStorageDeletionOfOneImageFails()
    {
        // A Cloudinary outage while deleting one image must not roll back the
        // already-committed property deletion, and must not stop the remaining
        // images from being cleaned up.
        var ownerId = Guid.NewGuid();
        var property = CreateProperty(ownerId);
        property.Images = new List<PropertyImage>
        {
            CreateImage(property.Id, "properties/fails"),
            CreateImage(property.Id, "properties/succeeds")
        };

        var (sut, storage, uow, _) = BuildSut(property, ownerId);

        storage
            .Setup(x => x.DeleteImageAsync("properties/fails", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cloudinary is unavailable."));

        var result = await sut.Handle(
            new DeletePropertyCommand(property.Id, ownerId, "127.0.0.1"),
            CancellationToken.None);

        Assert.True(result);

        storage.Verify(
            x => x.DeleteImageAsync("properties/succeeds", It.IsAny<CancellationToken>()),
            Times.Once);

        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PublishesPropertyDeletedEvent_ForALiveListingWithNoStatusTransition()
    {
        // Phase 1 audit F-10: deleting an Available listing directly never changes Status, so
        // PropertyStatusChangedEvent's transition map would never see it — this dedicated event
        // is what tells SocialDistribution to remove any live post.
        var ownerId = Guid.NewGuid();
        var property = CreateProperty(ownerId);
        var (sut, _, _, publisher) = BuildSut(property, ownerId);

        await sut.Handle(new DeletePropertyCommand(property.Id, ownerId, "127.0.0.1"), CancellationToken.None);

        publisher.Verify(
            x => x.Publish(
                It.Is<PropertyDeletedEvent>(e => e.PropertyId == property.Id && e.StatusAtDeletion == PropertyStatus.Available),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPropertyDeletedEventPublishThrows_StillSucceeds()
    {
        var ownerId = Guid.NewGuid();
        var property = CreateProperty(ownerId);
        var (sut, _, _, publisher) = BuildSut(property, ownerId);
        publisher
            .Setup(x => x.Publish(It.IsAny<PropertyDeletedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("subscriber blew up"));

        var result = await sut.Handle(new DeletePropertyCommand(property.Id, ownerId, "127.0.0.1"), CancellationToken.None);

        Assert.True(result);
    }

    private static (DeletePropertyCommandHandler Sut, Mock<IMediaStorageService> Storage, Mock<IUnitOfWork> Uow, Mock<IPublisher> Publisher)
        BuildSut(Property property, Guid ownerId)
    {
        var ownership = new Mock<IPropertyOwnershipService>();
        ownership
            .Setup(x => x.GetOwnedPropertyOrThrowAsync(
                property.Id,
                ownerId,
                "delete",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);

        var repo = new Mock<IPropertyRepository>();

        var imageRepo = new Mock<IPropertyImageRepository>();
        imageRepo
            .Setup(x => x.GetPropertyWithImagesAsync(property.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);

        var storage = new Mock<IMediaStorageService>();
        var uow = new Mock<IUnitOfWork>();
        var auditLogs = new Mock<IAuditLogService>();
        var publisher = new Mock<IPublisher>();
        var logger = new Mock<ILogger<DeletePropertyCommandHandler>>();

        var sut = new DeletePropertyCommandHandler(
            repo.Object,
            imageRepo.Object,
            ownership.Object,
            storage.Object,
            uow.Object,
            auditLogs.Object,
            publisher.Object,
            logger.Object);

        return (sut, storage, uow, publisher);
    }

    private static Property CreateProperty(Guid ownerId)
        => Property.Create(
            title: "Test listing",
            description: "A listing used only for this unit test.",
            ownerId: ownerId,
            listingType: ListingType.ForRent);

    private static PropertyImage CreateImage(Guid propertyId, string publicId)
        => new()
        {
            PropertyId = propertyId,
            Url = "https://res.cloudinary.com/demo/image/upload/" + publicId,
            PublicId = publicId
        };
}
