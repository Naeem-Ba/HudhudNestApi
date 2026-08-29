using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Application.Tests.ShortStay.Domain;

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
            new TimeOnly(14, 0), new TimeOnly(11, 0), 0m, 0m));
    }

    [Fact]
    public void Create_Throws_WhenCapacityLessThanOne()
    {
        Assert.Throws<DomainException>(() => ShortStayListing.Create(
            Guid.NewGuid(), 1, "title", "desc", 0, 1, 1,
            new TimeOnly(14, 0), new TimeOnly(11, 0), 0m, 0m));
    }

    [Fact]
    public void Publish_Throws_WhenNoRoomTypes()
    {
        var listing = CreateListing();
        Assert.Throws<DomainException>(() => listing.Publish());
    }

    [Fact]
    public void Publish_Succeeds_WhenRoomTypeExists()
    {
        var listing = CreateListing();
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
