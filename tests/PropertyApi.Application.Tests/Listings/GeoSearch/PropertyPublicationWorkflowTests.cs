using Moq;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Commands.PublishProperty;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Listings.Queries.GetPropertyForManagement;
using PropertyApi.Application.Listings.Queries.GetMyProperties;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Tests.Listings;

public sealed class PropertyPublicationWorkflowTests
{
    [Fact]
    public void NewProperties_ArePublishedByDefault()
    {
        var property = Property.Create(
            "Published property",
            "Published property description",
            Guid.NewGuid(),
            ListingType.ForRent);

        Assert.True(property.IsPublished);
        Assert.NotNull(property.PublishedAt);
    }

    [Fact]
    public async Task MyProperties_ReturnsDraftsOwnedByCurrentUser()
    {
        var property = CreateProperty();
        var repository = RepositoryReturning(property);
        repository
            .Setup(x => x.GetByOwnerAsync(property.OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([property]);
        var handler = new GetMyPropertiesQueryHandler(repository.Object);

        var result = await handler.Handle(
            new GetMyPropertiesQuery(property.OwnerId),
            CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal(property.Id, item.Id);
        Assert.False(item.IsPublished);
    }

    [Fact]
    public async Task Owner_CanPreviewOwnUnpublishedProperty()
    {
        var property = CreateProperty();
        var repository = RepositoryReturning(property);
        var handler = new GetPropertyForManagementQueryHandler(repository.Object);

        var result = await handler.Handle(
            new GetPropertyForManagementQuery(property.Id, property.OwnerId, IsAdmin: false),
            CancellationToken.None);

        Assert.Equal(property.Id, result.Id);
        Assert.False(result.IsPublished);
    }

    [Fact]
    public async Task AnotherUser_CannotPreviewUnpublishedProperty()
    {
        var property = CreateProperty();
        var repository = RepositoryReturning(property);
        var handler = new GetPropertyForManagementQueryHandler(repository.Object);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new GetPropertyForManagementQuery(property.Id, Guid.NewGuid(), IsAdmin: false),
            CancellationToken.None));
    }

    [Fact]
    public async Task PublishingCompleteProperty_MakesItPublicAndSetsDates()
    {
        var property = CreateProperty();
        property.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        property.Images.Add(new PropertyImage
        {
            PropertyId = property.Id,
            Url = "https://cdn.example/property.jpg",
            PublicId = "property"
        });

        var repository = RepositoryReturning(property);
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        var handler = new PublishPropertyCommandHandler(repository.Object, unitOfWork.Object);

        await handler.Handle(
            new PublishPropertyCommand(property.Id, property.OwnerId, IsAdmin: false),
            CancellationToken.None);

        Assert.True(property.IsPublished);
        Assert.NotNull(property.PublishedAt);
        Assert.True(
            property.ExpiresAt > DateTime.UtcNow.Add(ListingLifecyclePolicy.PublicationPeriod).AddMinutes(-10));
        unitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishingWithoutImages_IsRejected()
    {
        var property = CreateProperty();
        var repository = RepositoryReturning(property);
        var handler = new PublishPropertyCommandHandler(
            repository.Object,
            Mock.Of<IUnitOfWork>());

        var exception = await Assert.ThrowsAsync<ValidationException>(() => handler.Handle(
            new PublishPropertyCommand(property.Id, property.OwnerId, IsAdmin: false),
            CancellationToken.None));

        Assert.Contains("Images", exception.Errors.Keys);
        Assert.False(property.IsPublished);
    }

    [Fact]
    public async Task AnotherUser_CannotPublishProperty()
    {
        var property = CreateProperty();
        property.Images.Add(new PropertyImage
        {
            PropertyId = property.Id,
            Url = "https://cdn.example/property.jpg",
            PublicId = "property"
        });
        var repository = RepositoryReturning(property);
        var handler = new PublishPropertyCommandHandler(
            repository.Object,
            Mock.Of<IUnitOfWork>());

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new PublishPropertyCommand(property.Id, Guid.NewGuid(), IsAdmin: false),
            CancellationToken.None));
    }

    private static Property CreateProperty()
        => Property.Create(
            "Test property",
            "Complete property description",
            Guid.NewGuid(),
            ListingType.ForRent,
            "SY",
            "SYP",
            isPublished: false);

    private static Mock<IPropertyRepository> RepositoryReturning(Property property)
    {
        var repository = new Mock<IPropertyRepository>();
        repository
            .Setup(x => x.GetByIdWithDetailsAsync(property.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);
        return repository;
    }
}
