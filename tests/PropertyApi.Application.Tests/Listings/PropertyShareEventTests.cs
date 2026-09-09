using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.Listings.Enums;

namespace PropertyApi.Application.Tests.Listings;

public sealed class PropertyShareEventTests
{
    [Fact]
    public void Create_WithAnonymousVisitor_Succeeds()
    {
        var propertyId = Guid.NewGuid();

        var evt = PropertyShareEvent.Create(propertyId, SharePlatform.WhatsApp);

        Assert.Equal(propertyId, evt.PropertyId);
        Assert.Equal(SharePlatform.WhatsApp, evt.Platform);
        Assert.Null(evt.UserId);
    }

    [Fact]
    public void Create_WithLoggedInUser_StoresUserId()
    {
        var propertyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var evt = PropertyShareEvent.Create(propertyId, SharePlatform.Facebook, userId);

        Assert.Equal(userId, evt.UserId);
    }

    [Fact]
    public void Create_WithEmptyPropertyId_Throws()
    {
        Assert.Throws<DomainException>(() => PropertyShareEvent.Create(Guid.Empty, SharePlatform.CopyLink));
    }

    [Fact]
    public void Create_WithUndefinedPlatform_Throws()
    {
        Assert.Throws<DomainException>(() => PropertyShareEvent.Create(Guid.NewGuid(), (SharePlatform)999));
    }

    // ── UTM / Attribution (Phase 2) ─────────────────────────────────────────

    [Fact]
    public void Create_WithValidUtmFields_StoresThemAll()
    {
        var evt = PropertyShareEvent.Create(
            Guid.NewGuid(),
            SharePlatform.Facebook,
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
    public void Create_WithNoUtmFields_LeavesThemNull()
    {
        var evt = PropertyShareEvent.Create(Guid.NewGuid(), SharePlatform.Native);

        Assert.Null(evt.UtmSource);
        Assert.Null(evt.UtmMedium);
        Assert.Null(evt.UtmCampaign);
        Assert.Null(evt.UtmContent);
    }

    [Fact]
    public void Create_TrimsWhitespaceAroundUtmFields()
    {
        var evt = PropertyShareEvent.Create(Guid.NewGuid(), SharePlatform.WhatsApp, utmSource: "  whatsapp  ");

        Assert.Equal("whatsapp", evt.UtmSource);
    }

    [Fact]
    public void Create_WithUtmFieldContainingSpaces_DropsThatFieldOnly()
    {
        // Spaces are not a valid UTM character (section 4: "لا تحتوي على مسافات") — the rest of
        // the event must still be recorded, not rejected wholesale over one bad field.
        var evt = PropertyShareEvent.Create(
            Guid.NewGuid(),
            SharePlatform.Facebook,
            utmSource: "facebook",
            utmCampaign: "property share");

        Assert.Equal("facebook", evt.UtmSource);
        Assert.Null(evt.UtmCampaign);
    }

    [Fact]
    public void Create_WithUtmFieldContainingHtmlOrScriptCharacters_DropsThatFieldOnly()
    {
        var evt = PropertyShareEvent.Create(
            Guid.NewGuid(),
            SharePlatform.Facebook,
            utmContent: "<script>alert(1)</script>");

        Assert.Null(evt.UtmContent);
    }

    [Fact]
    public void Create_WithUtmFieldOver60Characters_DropsThatFieldOnly()
    {
        var tooLong = new string('a', 61);

        var evt = PropertyShareEvent.Create(Guid.NewGuid(), SharePlatform.Facebook, utmContent: tooLong);

        Assert.Null(evt.UtmContent);
    }

    [Fact]
    public void Create_WithUtmFieldExactly60Characters_KeepsIt()
    {
        var exactly60 = new string('a', 60);

        var evt = PropertyShareEvent.Create(Guid.NewGuid(), SharePlatform.Facebook, utmContent: exactly60);

        Assert.Equal(exactly60, evt.UtmContent);
    }

    [Fact]
    public void Create_WithEmptyOrWhitespaceOnlyUtmField_LeavesItNull()
    {
        var evt = PropertyShareEvent.Create(Guid.NewGuid(), SharePlatform.Facebook, utmSource: "   ");

        Assert.Null(evt.UtmSource);
    }
}
