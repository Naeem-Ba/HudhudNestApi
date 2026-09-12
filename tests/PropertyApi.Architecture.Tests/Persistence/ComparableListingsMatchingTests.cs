using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Infrastructure.Repositories;

namespace PropertyApi.Architecture.Tests.Persistence;

/// <summary>
/// Valuation Fast Path (Phase 3) — the actual DB-side matching RULES (PropertyType,
/// hierarchical Location, Area ±20%, ListingType, Active/Published eligibility, and price
/// resolution). Lives here, not in PropertyApi.Application.Tests, for the exact reason
/// PropertySortOrderTests' own doc comment gives: PropertyRepository.
/// ApplyComparableListingsFilter/ResolveComparablePrice live in Infrastructure, and
/// PropertyApi.Application.Tests deliberately does not reference it.
///
/// ApplyComparableListingsFilter is exercised over LINQ-to-Objects (a plain in-memory list),
/// exactly like ApplySort is in PropertySortOrderTests — that checks the rule itself, not its
/// SQL translation, with no database involved. GetComparableListingsQueryHandlerTests (in
/// PropertyApi.Application.Tests) separately covers the Handler's own calculation/decision
/// logic (Min/Max/Average/Median, Preliminary vs. Stage-4 handoff) against a repository stub.
/// </summary>
public sealed class ComparableListingsMatchingTests
{
    private static readonly DateTime Now = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private const decimal AreaTolerancePercent = 20m;

    // ── PropertyType ──────────────────────────────────────────────

    [Fact]
    public void Excludes_PropertyWithADifferentPropertyType()
    {
        var match = Listing(propertyTypeId: 1);
        var mismatch = Listing(propertyTypeId: 2);

        var results = Filter([match, mismatch], propertyTypeId: 1);

        Assert.Equal([match], results);
    }

    [Fact]
    public void PropertyTypeFilter_IsSkipped_WhenInquiryHasNone()
    {
        var typeA = Listing(propertyTypeId: 1);
        var typeB = Listing(propertyTypeId: 2);

        var results = Filter([typeA, typeB], propertyTypeId: null);

        Assert.Equal(2, results.Count);
    }

    // ── ListingType ───────────────────────────────────────────────

    [Fact]
    public void Excludes_RentalListing_WhenInquiryIsForSale()
    {
        var forSale = Listing(listingType: ListingType.ForSale, purchasePrice: 100_000m);
        var forRent = Listing(listingType: ListingType.ForRent, coldRent: 500m);

        var results = Filter([forSale, forRent], listingType: ListingType.ForSale);

        Assert.Equal([forSale], results);
    }

    [Fact]
    public void Excludes_SaleListing_WhenInquiryIsForRent()
    {
        var forSale = Listing(listingType: ListingType.ForSale, purchasePrice: 100_000m);
        var forRent = Listing(listingType: ListingType.ForRent, coldRent: 500m);

        var results = Filter([forSale, forRent], listingType: ListingType.ForRent);

        Assert.Equal([forRent], results);
    }

    // ── Area boundaries (±20%) ────────────────────────────────────

    [Theory]
    [InlineData(80)]  // exactly -20%
    [InlineData(120)] // exactly +20%
    public void Includes_PropertyAtTheExactAreaBoundary(decimal area)
    {
        var listing = Listing(area: area);

        var results = Filter([listing], inquiryArea: 100m);

        Assert.Equal([listing], results);
    }

    [Theory]
    [InlineData(79)]
    [InlineData(121)]
    public void Excludes_PropertyJustOutsideTheAreaBoundary(decimal area)
    {
        var listing = Listing(area: area);

        var results = Filter([listing], inquiryArea: 100m);

        Assert.Empty(results);
    }

    [Fact]
    public void AreaFilter_IsSkipped_WhenInquiryHasNoArea()
    {
        var tiny = Listing(area: 10m);
        var huge = Listing(area: 10_000m);

        var results = Filter([tiny, huge], inquiryArea: null);

        Assert.Equal(2, results.Count);
    }

    // ── Location hierarchy ────────────────────────────────────────

    [Fact]
    public void Neighborhood_MatchesExactNeighborhoodOnly_EvenWithinTheSameDistrict()
    {
        var sameNeighborhood = Listing(governorateId: 1, districtId: 10, neighborhoodId: 100);
        var sameDistrictDifferentNeighborhood = Listing(governorateId: 1, districtId: 10, neighborhoodId: 101);

        var results = Filter(
            [sameNeighborhood, sameDistrictDifferentNeighborhood],
            governorateId: 1, districtId: 10, neighborhoodId: 100);

        Assert.Equal([sameNeighborhood], results);
    }

    [Fact]
    public void District_IsUsed_WhenNeighborhoodIsAbsent()
    {
        var sameDistrict = Listing(governorateId: 1, districtId: 10, neighborhoodId: 999);
        var otherDistrict = Listing(governorateId: 1, districtId: 20, neighborhoodId: 999);

        var results = Filter(
            [sameDistrict, otherDistrict],
            governorateId: 1, districtId: 10, neighborhoodId: null);

        Assert.Equal([sameDistrict], results);
    }

    [Fact]
    public void Governorate_IsUsed_WhenDistrictAndNeighborhoodAreBothAbsent()
    {
        var sameGovernorate = Listing(governorateId: 1, districtId: 55, neighborhoodId: 555);
        var otherGovernorate = Listing(governorateId: 2, districtId: 55, neighborhoodId: 555);

        var results = Filter(
            [sameGovernorate, otherGovernorate],
            governorateId: 1, districtId: null, neighborhoodId: null);

        Assert.Equal([sameGovernorate], results);
    }

    [Fact]
    public void Excludes_PropertyInADifferentGovernorate_EvenWhenEverythingElseMatches()
    {
        // Section 7's explicit warning: never cross a governorate boundary, no matter how
        // good the rest of the match looks.
        var wrongGovernorate = Listing(
            governorateId: 2, districtId: 10, neighborhoodId: 100,
            propertyTypeId: 1, area: 100m, listingType: ListingType.ForSale, purchasePrice: 100_000m);

        var results = Filter(
            [wrongGovernorate],
            propertyTypeId: 1, inquiryArea: 100m, listingType: ListingType.ForSale,
            governorateId: 1, districtId: 10, neighborhoodId: 100);

        Assert.Empty(results);
    }

    [Fact]
    public void NeighborhoodMatch_NeverImplicitlyWidensToDistrict_JustBecauseFewResults()
    {
        // Only one property shares the neighborhood; several more share only the district.
        // The neighborhood-level query must return just the one match, not silently widen.
        var inNeighborhood = Listing(governorateId: 1, districtId: 10, neighborhoodId: 100);
        var districtOnly1 = Listing(governorateId: 1, districtId: 10, neighborhoodId: 101);
        var districtOnly2 = Listing(governorateId: 1, districtId: 10, neighborhoodId: 102);

        var results = Filter(
            [inNeighborhood, districtOnly1, districtOnly2],
            governorateId: 1, districtId: 10, neighborhoodId: 100);

        Assert.Equal([inNeighborhood], results);
    }

    // ── Active / Published eligibility ───────────────────────────

    [Fact]
    public void Excludes_UnpublishedListing()
    {
        var unpublished = Listing(isPublished: false);

        var results = Filter([unpublished]);

        Assert.Empty(results);
    }

    [Fact]
    public void Excludes_ExpiredListing()
    {
        var expired = Listing(expiresAt: Now.AddDays(-1));

        var results = Filter([expired]);

        Assert.Empty(results);
    }

    [Fact]
    public void Includes_PublishedListingWithNoExpiry()
    {
        var listing = Listing(expiresAt: null);

        var results = Filter([listing]);

        Assert.Equal([listing], results);
    }

    [Fact]
    public void Includes_PublishedListingWhoseExpiryIsStillInTheFuture()
    {
        var listing = Listing(expiresAt: Now.AddDays(1));

        var results = Filter([listing]);

        Assert.Equal([listing], results);
    }

    // ── Price resolution / exclusion ──────────────────────────────

    [Theory]
    [InlineData(ListingType.ForSale)]
    [InlineData(ListingType.ForRent)]
    public void ResolveComparablePrice_ExcludesAMissingPrice_AsNull(ListingType listingType)
    {
        // No price fields set at all — PurchasePrice must be explicitly nulled here since
        // the Listing() helper otherwise defaults it for the ForSale-typical case.
        var listing = Listing(listingType: listingType, purchasePrice: null);

        var price = PropertyRepository.ResolveComparablePrice(listing, listingType);

        Assert.Null(price);
    }

    [Fact]
    public void ResolveComparablePrice_ForSale_UsesPurchasePrice()
    {
        var listing = Listing(listingType: ListingType.ForSale, purchasePrice: 250_000m);

        Assert.Equal(250_000m, PropertyRepository.ResolveComparablePrice(listing, ListingType.ForSale));
    }

    [Fact]
    public void ResolveComparablePrice_ForRent_PrefersColdRent_FallsBackToWarmRent()
    {
        var withColdRent = Listing(listingType: ListingType.ForRent, coldRent: 400m, warmRent: 550m);
        var withOnlyWarmRent = Listing(listingType: ListingType.ForRent, warmRent: 600m);

        Assert.Equal(400m, PropertyRepository.ResolveComparablePrice(withColdRent, ListingType.ForRent));
        Assert.Equal(600m, PropertyRepository.ResolveComparablePrice(withOnlyWarmRent, ListingType.ForRent));
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static List<Property> Filter(
        IEnumerable<Property> listings,
        int? propertyTypeId = 1,
        ListingType listingType = ListingType.ForSale,
        decimal? inquiryArea = 100m,
        int governorateId = 1,
        int? districtId = 10,
        int? neighborhoodId = 100)
    {
        return PropertyRepository.ApplyComparableListingsFilter(
            listings.AsQueryable(),
            propertyTypeId,
            listingType,
            inquiryArea,
            AreaTolerancePercent,
            governorateId,
            districtId,
            neighborhoodId,
            Now).ToList();
    }

    private static Property Listing(
        int? propertyTypeId = 1,
        ListingType listingType = ListingType.ForSale,
        decimal area = 100m,
        int governorateId = 1,
        int? districtId = 10,
        int? neighborhoodId = 100,
        decimal? purchasePrice = 100_000m,
        decimal? coldRent = null,
        decimal? warmRent = null,
        bool isPublished = true,
        DateTime? expiresAt = null)
    {
        var property = Property.Create(
            title: "Comparable listing fixture",
            description: "Used only by ComparableListingsMatchingTests",
            ownerId: Guid.NewGuid(),
            listingType: listingType,
            countryCode: "SY",
            currencyCode: "SYP",
            isPublished: isPublished);

        property.PropertyTypeId = propertyTypeId;
        property.Area = area;
        property.GovernorateId = governorateId;
        property.DistrictId = districtId;
        property.NeighborhoodId = neighborhoodId;
        property.PurchasePrice = purchasePrice;
        property.ColdRent = coldRent;
        property.WarmRent = warmRent;
        property.ExpiresAt = expiresAt;

        return property;
    }
}
