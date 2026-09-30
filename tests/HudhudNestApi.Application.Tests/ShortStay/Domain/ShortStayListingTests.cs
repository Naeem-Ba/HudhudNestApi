using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Domain.ShortStay.Enums;

namespace HudhudNestApi.Application.Tests.ShortStay.Domain;

public sealed class ShortStayListingTests
{
    private static ShortStayListing CreateListing() =>
        ShortStayListing.Create(
            ownerId: Guid.NewGuid(),
            accommodationTypeId: 1,
            title: "شاليه على البحر",
            description: "إطلالة رائعة",
            capacity: 4,
            bedrooms: 2,
            bathrooms: 1,
            checkInTime: new TimeOnly(14, 0),
            checkOutTime: new TimeOnly(11, 0),
            latitude: 34.9m,
            longitude: 35.9m);

    [Fact]
    public void Create_Throws_WhenTitleBlank()
    {
        Assert.Throws<DomainException>(() => ShortStayListing.Create(
            Guid.NewGuid(), 1, "   ", "desc", 2, 1, 1,
            new TimeOnly(14, 0), new TimeOnly(11, 0), 33.5m, 36.3m));
    }

    [Fact]
    public void Create_Throws_WhenCapacityLessThanOne()
    {
        Assert.Throws<DomainException>(() => ShortStayListing.Create(
            Guid.NewGuid(), 1, "title", "desc", 0, 1, 1,
            new TimeOnly(14, 0), new TimeOnly(11, 0), 33.5m, 36.3m));
    }

    [Fact]
    public void Create_DefaultsCurrencyToSyp()
    {
        Assert.Equal("SYP", CreateListing().CurrencyCode);
    }

    [Theory]
    [InlineData("usd", "USD")]
    [InlineData(" eur ", "EUR")]
    public void Create_NormalizesSupportedCurrency(string input, string expected)
    {
        var listing = ShortStayListing.Create(
            Guid.NewGuid(), 1, "title", "desc", 2, 1, 1,
            new TimeOnly(14, 0), new TimeOnly(11, 0), 33.5m, 36.3m, currencyCode: input);

        Assert.Equal(expected, listing.CurrencyCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("XXX")]
    [InlineData("US")]
    public void Create_Throws_WhenCurrencyUnsupported(string code)
    {
        Assert.Throws<DomainException>(() => ShortStayListing.Create(
            Guid.NewGuid(), 1, "title", "desc", 2, 1, 1,
            new TimeOnly(14, 0), new TimeOnly(11, 0), 33.5m, 36.3m, currencyCode: code));
    }

    [Fact]
    public void Publish_Throws_WhenNoRoomTypes()
    {
        var listing = CreateListing();
        Assert.Throws<DomainException>(() => listing.Publish());
    }

    private static ShortStayListing CreateLocatedListing()
    {
        var listing = CreateListing();
        listing.UpdateLocation(34.9m, 35.9m, 1, null, null, "دمشق", LocationVisibility.Exact);
        return listing;
    }

    [Fact]
    public void Create_WithoutCoordinates_LeavesThemNullNotZero()
    {
        var listing = ShortStayListing.Create(
            Guid.NewGuid(), 1, "title", "desc", 2, 1, 1, new TimeOnly(14, 0), new TimeOnly(11, 0), null, null);

        Assert.Null(listing.Latitude);
        Assert.Null(listing.Longitude);
        Assert.False(listing.HasLocation);
    }

    [Theory]
    [InlineData(0.0, 0.0)]      // the "no location" default clients send
    [InlineData(91.0, 10.0)]
    [InlineData(10.0, 181.0)]
    [InlineData(33.5, null)]  // half a pin
    [InlineData(null, 36.3)]
    public void UpdateLocation_RejectsAnInvalidPin(double? lat, double? lng)
    {
        var listing = CreateListing();
        Assert.Throws<DomainException>(() => listing.UpdateLocation(
            (decimal?)lat, (decimal?)lng, null, null, null, "دمشق", LocationVisibility.Exact));
    }

    [Fact]
    public void Publish_Throws_WhenLocationIsMissing()
    {
        var listing = CreateListing(); // has a pin but no city
        listing.RoomTypes.Add(new RoomType { Name = "الوحدة الافتراضية", BasePricePerNight = 100m });

        Assert.Throws<DomainException>(() => listing.Publish());

        listing.UpdateLocation(null, null, null, null, null, "دمشق", LocationVisibility.Exact); // city but no pin
        Assert.Throws<DomainException>(() => listing.Publish());
    }

    [Fact]
    public void UpdateLocation_Throws_WhenClearingTheLocationOfAPublishedListing()
    {
        var listing = CreateLocatedListing();
        listing.RoomTypes.Add(new RoomType { Name = "الوحدة الافتراضية", BasePricePerNight = 100m });
        listing.Publish();

        Assert.Throws<DomainException>(() => listing.UpdateLocation(
            null, null, null, null, null, null, LocationVisibility.Exact));
        Assert.True(listing.HasLocation);
    }

    [Fact]
    public void ApproximateCoordinate_SnapsToAboutOneKilometre()
    {
        Assert.Equal(33.51m, ShortStayListing.ApproximateCoordinate(33.512345m));
        Assert.Equal(36.31m, ShortStayListing.ApproximateCoordinate(36.306789m));
        Assert.Null(ShortStayListing.ApproximateCoordinate(null));
    }

    [Fact]
    public void Publish_Succeeds_WhenRoomTypeExists()
    {
        var listing = CreateLocatedListing();
        listing.RoomTypes.Add(new RoomType { Name = "الوحدة الافتراضية", BasePricePerNight = 100m });

        listing.Publish();

        Assert.True(listing.IsPublished);
        Assert.NotNull(listing.PublishedAt);
    }

    [Fact]
    public void UpdateBookingSettings_Throws_WhenBothModesDisabled()
    {
        var listing = CreateListing();
        Assert.Throws<DomainException>(() => listing.UpdateBookingSettings(false, false));
    }

    [Fact]
    public void BuildHouseRulesSnapshotText_IncludesCustomRulesAndQuietHours()
    {
        var listing = CreateListing();
        listing.UpdateHouseRules(
            allowsSmoking: false,
            allowsParties: false,
            allowsPets: true,
            quietHoursStart: new TimeOnly(22, 0),
            quietHoursEnd: new TimeOnly(8, 0),
            customRulesText: "الرجاء خلع الأحذية عند الدخول");

        var snapshot = listing.BuildHouseRulesSnapshotText();

        Assert.Contains("الحيوانات الأليفة مسموحة", snapshot);
        Assert.Contains("22:00", snapshot);
        Assert.Contains("الرجاء خلع الأحذية عند الدخول", snapshot);
    }

    [Fact]
    public void SetAmenities_ReplacesEntireSet_WithoutDuplicates()
    {
        var listing = CreateListing();
        var wifi = Guid.NewGuid();
        var pool = Guid.NewGuid();

        listing.SetAmenities([wifi, wifi, pool]);
        Assert.Equal(2, listing.ListingAmenities.Count);
        Assert.Contains(listing.ListingAmenities, a => a.AmenityId == wifi);
        Assert.Contains(listing.ListingAmenities, a => a.AmenityId == pool);

        var parking = Guid.NewGuid();
        listing.SetAmenities([parking]);

        Assert.Single(listing.ListingAmenities);
        Assert.Equal(parking, listing.ListingAmenities.Single().AmenityId);
    }
}
