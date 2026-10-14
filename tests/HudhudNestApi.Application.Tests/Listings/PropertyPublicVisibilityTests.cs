using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Tests.Listings;

/// <summary>
/// Who can see a listing by its status (QA Phase 7, P7-D08). Archived used to stay public because
/// only IsPublished and ExpiresAt were checked; these pin the table agreed with the owner.
/// </summary>
public sealed class PropertyPublicVisibilityTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private static Property Listing(PropertyStatus status, bool published = true, DateTime? expiresAt = null)
    {
        var property = Property.Create("title", "description long enough", Guid.NewGuid(), ListingType.ForSale);
        property.Status = status;
        property.ExpiresAt = expiresAt;
        if (!published) property.Unpublish();
        return property;
    }

    [Theory(DisplayName = "Search shows Available and Reserved listings only")]
    [InlineData(PropertyStatus.Available, true)]
    [InlineData(PropertyStatus.Reserved, true)]
    [InlineData(PropertyStatus.Archived, false)]
    [InlineData(PropertyStatus.Sold, false)]
    [InlineData(PropertyStatus.Rented, false)]
    [InlineData(PropertyStatus.Expired, false)]
    public void OnMarket_ByStatus(PropertyStatus status, bool expected)
    {
        Assert.Equal(expected, Property.OnMarket(Now).Compile()(Listing(status)));
    }

    [Fact]
    public void OnMarket_RequiresPublicationAndAnOpenWindow()
    {
        var onMarket = Property.OnMarket(Now).Compile();

        Assert.False(onMarket(Listing(PropertyStatus.Available, published: false)));
        Assert.False(onMarket(Listing(PropertyStatus.Reserved, expiresAt: Now.AddMinutes(-1))));
        Assert.True(onMarket(Listing(PropertyStatus.Reserved, expiresAt: Now.AddDays(3))));
    }

    [Theory(DisplayName = "The detail page opens for market listings and closed deals, never for Archived or Expired")]
    [InlineData(PropertyStatus.Available, true)]
    [InlineData(PropertyStatus.Reserved, true)]
    [InlineData(PropertyStatus.Sold, true)]
    [InlineData(PropertyStatus.Rented, true)]
    [InlineData(PropertyStatus.Archived, false)]
    [InlineData(PropertyStatus.Expired, false)]
    public void PubliclyViewable_ByStatus(PropertyStatus status, bool expected)
    {
        Assert.Equal(expected, Property.PubliclyViewable(Now).Compile()(Listing(status)));
    }

    [Fact(DisplayName = "A closed deal stays viewable after its publication window; an unpublished one does not")]
    public void ClosedDeal_IgnoresTheWindow_ButNotPublication()
    {
        var viewable = Property.PubliclyViewable(Now).Compile();

        Assert.True(viewable(Listing(PropertyStatus.Sold, expiresAt: Now.AddDays(-400))));
        Assert.False(viewable(Listing(PropertyStatus.Sold, published: false)));
        Assert.False(viewable(Listing(PropertyStatus.Available, expiresAt: Now.AddDays(-1))));
    }

    [Theory]
    [InlineData(PropertyStatus.Sold, PropertyStatus.Sold, true)]
    [InlineData(PropertyStatus.Sold, PropertyStatus.Rented, false)]
    [InlineData(PropertyStatus.Rented, PropertyStatus.Rented, true)]
    [InlineData(PropertyStatus.Available, PropertyStatus.Sold, false)]
    public void ClosedDealOf_MatchesOnlyThatStatus(PropertyStatus actual, PropertyStatus tab, bool expected)
    {
        Assert.Equal(expected, Property.ClosedDealOf(tab).Compile()(Listing(actual)));
    }

    [Fact(DisplayName = "Closing a deal stamps ClosedAt; reopening it clears the stamp so compression restarts later")]
    public void ChangeStatus_TracksClosedAt()
    {
        var property = Listing(PropertyStatus.Available);
        Assert.Null(property.ClosedAt);

        property.ChangeStatus(PropertyStatus.Reserved);
        Assert.Null(property.ClosedAt);

        property.ChangeStatus(PropertyStatus.Sold);
        var closedAt = Assert.NotNull(property.ClosedAt);

        // Sold -> Rented is still one closed deal: the original closing date is kept.
        property.ChangeStatus(PropertyStatus.Rented);
        Assert.Equal(closedAt, property.ClosedAt);

        // The buyer backed out.
        property.ChangeStatus(PropertyStatus.Available);
        Assert.Null(property.ClosedAt);
    }

    [Fact]
    public void MarkImagesCompacted_StampsOnceAndSurvivesReopening()
    {
        var property = Listing(PropertyStatus.Sold);
        property.MarkImagesCompacted();
        var stamp = property.ImagesCompactedAt;
        Assert.NotNull(stamp);

        property.ChangeStatus(PropertyStatus.Available);

        // Kept on purpose: the photos are already small, so a later sale must not recompress them.
        Assert.Equal(stamp, property.ImagesCompactedAt);
    }
}
