using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Commands.TrackPropertyShareEvent;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Listings.Enums;

namespace HudhudNestApi.Application.Tests.Listings;

public sealed class TrackPropertyShareEventCommandHandlerTests
{
    [Fact]
    public async Task Handle_PropertyIsPubliclyVisible_PersistsEventAndSaves()
    {
        var propertyId = Guid.NewGuid();

        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.IsPubliclyVisibleAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var shareEvents = new Mock<IPropertyShareEventRepository>();
        PropertyShareEvent? added = null;
        shareEvents.Setup(x => x.Add(It.IsAny<PropertyShareEvent>()))
            .Callback<PropertyShareEvent>(e => added = e);

        var uow = new Mock<IUnitOfWork>();

        var handler = new TrackPropertyShareEventCommandHandler(properties.Object, shareEvents.Object, uow.Object);

        var result = await handler.Handle(
            new TrackPropertyShareEventCommand(propertyId, SharePlatform.Telegram, UserId: null),
            CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(result, added!.Id);
        Assert.Equal(propertyId, added.PropertyId);
        Assert.Equal(SharePlatform.Telegram, added.Platform);
        Assert.Null(added.UserId);
        shareEvents.Verify(x => x.Add(It.IsAny<PropertyShareEvent>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WithAuthenticatedUser_StoresUserId()
    {
        var propertyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.IsPubliclyVisibleAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var shareEvents = new Mock<IPropertyShareEventRepository>();
        PropertyShareEvent? added = null;
        shareEvents.Setup(x => x.Add(It.IsAny<PropertyShareEvent>()))
            .Callback<PropertyShareEvent>(e => added = e);

        var uow = new Mock<IUnitOfWork>();
        var handler = new TrackPropertyShareEventCommandHandler(properties.Object, shareEvents.Object, uow.Object);

        await handler.Handle(
            new TrackPropertyShareEventCommand(propertyId, SharePlatform.CopyLink, userId),
            CancellationToken.None);

        Assert.Equal(userId, added!.UserId);
    }

    [Fact]
    public async Task Handle_PropertyNotPubliclyVisible_ThrowsNotFoundAndNeverPersists()
    {
        var propertyId = Guid.NewGuid();

        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.IsPubliclyVisibleAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var shareEvents = new Mock<IPropertyShareEventRepository>();
        var uow = new Mock<IUnitOfWork>();
        var handler = new TrackPropertyShareEventCommandHandler(properties.Object, shareEvents.Object, uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new TrackPropertyShareEventCommand(propertyId, SharePlatform.Native, UserId: null),
            CancellationToken.None));

        shareEvents.Verify(x => x.Add(It.IsAny<PropertyShareEvent>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithUtmFields_PassesThemThroughToTheEvent()
    {
        var propertyId = Guid.NewGuid();

        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.IsPubliclyVisibleAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var shareEvents = new Mock<IPropertyShareEventRepository>();
        PropertyShareEvent? added = null;
        shareEvents.Setup(x => x.Add(It.IsAny<PropertyShareEvent>()))
            .Callback<PropertyShareEvent>(e => added = e);

        var uow = new Mock<IUnitOfWork>();
        var handler = new TrackPropertyShareEventCommandHandler(properties.Object, shareEvents.Object, uow.Object);

        await handler.Handle(
            new TrackPropertyShareEventCommand(
                propertyId,
                SharePlatform.WhatsApp,
                UserId: null,
                UtmSource: "whatsapp",
                UtmMedium: "social",
                UtmCampaign: "property_share",
                UtmContent: "property_123"),
            CancellationToken.None);

        Assert.Equal("whatsapp", added!.UtmSource);
        Assert.Equal("social", added.UtmMedium);
        Assert.Equal("property_share", added.UtmCampaign);
        Assert.Equal("property_123", added.UtmContent);
    }
}
