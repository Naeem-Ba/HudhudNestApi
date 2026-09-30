using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Listings.Enums;

namespace HudhudNestApi.Application.Tests.Listings;

public sealed class PropertyAttributionEventTests
{
    [Fact]
    public void Create_WithAnonymousVisitor_Succeeds()
    {
        var propertyId = Guid.NewGuid();

        var evt = PropertyAttributionEvent.Create(propertyId, PropertyAttributionEventType.View);

        Assert.Equal(propertyId, evt.PropertyId);
        Assert.Equal(PropertyAttributionEventType.View, evt.EventType);
        Assert.Null(evt.UserId);
    }

    [Fact]
    public void Create_WithLoggedInUser_StoresUserId()
    {
        var userId = Guid.NewGuid();

        var evt = PropertyAttributionEvent.Create(Guid.NewGuid(), PropertyAttributionEventType.MessageSent, userId);

        Assert.Equal(userId, evt.UserId);
    }

    [Fact]
    public void Create_WithEmptyPropertyId_Throws()
    {
        Assert.Throws<DomainException>(() =>
            PropertyAttributionEvent.Create(Guid.Empty, PropertyAttributionEventType.View));
    }

    [Fact]
    public void Create_WithUndefinedEventType_Throws()
    {
        Assert.Throws<DomainException>(() =>
            PropertyAttributionEvent.Create(Guid.NewGuid(), (PropertyAttributionEventType)999));
    }

    [Fact]
    public void Create_WithNoUtmFields_RepresentsADirectVisit()
    {
        // A direct visit (no UTM parameters at all) must not fabricate a fake social attribution.
        var evt = PropertyAttributionEvent.Create(Guid.NewGuid(), PropertyAttributionEventType.View);

        Assert.Null(evt.UtmSource);
        Assert.Null(evt.UtmMedium);
        Assert.Null(evt.UtmCampaign);
        Assert.Null(evt.UtmContent);
    }

    [Fact]
    public void Create_WithValidUtmFields_StoresThemAll()
    {
        var evt = PropertyAttributionEvent.Create(
            Guid.NewGuid(),
            PropertyAttributionEventType.VisitRequestCreated,
            utmSource: "facebook",
            utmMedium: "social",
            utmCampaign: "property_share",
            utmContent: "property_123");

        Assert.Equal("facebook", evt.UtmSource);
        Assert.Equal("social", evt.UtmMedium);
        Assert.Equal("property_share", evt.UtmCampaign);
        Assert.Equal("property_123", evt.UtmContent);
    }

    [Fact]
    public void Create_WithInvalidUtmField_DropsThatFieldOnly()
    {
        var evt = PropertyAttributionEvent.Create(
            Guid.NewGuid(),
            PropertyAttributionEventType.PhoneClick,
            utmSource: "facebook",
            utmContent: "<script>alert(1)</script>");

        Assert.Equal("facebook", evt.UtmSource);
        Assert.Null(evt.UtmContent);
    }
}
