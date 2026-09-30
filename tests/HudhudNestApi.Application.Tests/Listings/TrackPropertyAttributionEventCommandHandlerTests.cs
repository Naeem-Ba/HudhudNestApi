using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Commands.TrackPropertyAttributionEvent;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Listings.Enums;

namespace HudhudNestApi.Application.Tests.Listings;

public sealed class TrackPropertyAttributionEventCommandHandlerTests
{
    [Fact]
    public async Task Handle_PropertyIsPubliclyVisible_PersistsEventAndSaves()
    {
        var propertyId = Guid.NewGuid();

        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.IsPubliclyVisibleAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var attributionEvents = new Mock<IPropertyAttributionEventRepository>();
        PropertyAttributionEvent? added = null;
        attributionEvents.Setup(x => x.Add(It.IsAny<PropertyAttributionEvent>()))
            .Callback<PropertyAttributionEvent>(e => added = e);

        var uow = new Mock<IUnitOfWork>();
        var handler = new TrackPropertyAttributionEventCommandHandler(properties.Object, attributionEvents.Object, uow.Object);

        var result = await handler.Handle(
            new TrackPropertyAttributionEventCommand(
                propertyId,
                PropertyAttributionEventType.View,
                UserId: null,
                UtmSource: "facebook",
                UtmMedium: "social",
                UtmCampaign: "property_share",
                UtmContent: "property_123"),
            CancellationToken.None);

        Assert.NotNull(added);
        Assert.Equal(result, added!.Id);
        Assert.Equal(propertyId, added.PropertyId);
        Assert.Equal(PropertyAttributionEventType.View, added.EventType);
        Assert.Equal("facebook", added.UtmSource);
        attributionEvents.Verify(x => x.Add(It.IsAny<PropertyAttributionEvent>()), Times.Once);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_DirectVisitWithNoUtmFields_PersistsEventWithNullAttribution()
    {
        var propertyId = Guid.NewGuid();

        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.IsPubliclyVisibleAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var attributionEvents = new Mock<IPropertyAttributionEventRepository>();
        PropertyAttributionEvent? added = null;
        attributionEvents.Setup(x => x.Add(It.IsAny<PropertyAttributionEvent>()))
            .Callback<PropertyAttributionEvent>(e => added = e);

        var uow = new Mock<IUnitOfWork>();
        var handler = new TrackPropertyAttributionEventCommandHandler(properties.Object, attributionEvents.Object, uow.Object);

        await handler.Handle(
            new TrackPropertyAttributionEventCommand(
                propertyId, PropertyAttributionEventType.View, null, null, null, null, null),
            CancellationToken.None);

        Assert.Null(added!.UtmSource);
        attributionEvents.Verify(x => x.Add(It.IsAny<PropertyAttributionEvent>()), Times.Once);
    }

    [Fact]
    public async Task Handle_PropertyNotPubliclyVisible_ThrowsNotFoundAndNeverPersists()
    {
        var propertyId = Guid.NewGuid();

        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.IsPubliclyVisibleAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var attributionEvents = new Mock<IPropertyAttributionEventRepository>();
        var uow = new Mock<IUnitOfWork>();
        var handler = new TrackPropertyAttributionEventCommandHandler(properties.Object, attributionEvents.Object, uow.Object);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new TrackPropertyAttributionEventCommand(
                propertyId, PropertyAttributionEventType.VisitRequestCreated, null, null, null, null, null),
            CancellationToken.None));

        attributionEvents.Verify(x => x.Add(It.IsAny<PropertyAttributionEvent>()), Times.Never);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
