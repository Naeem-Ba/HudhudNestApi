using HudhudNestApi.Application.Demand.Commands;
using HudhudNestApi.Application.Demand.Matching;
using HudhudNestApi.Application.Listings.Currencies;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Demand.Entities;
using HudhudNestApi.Domain.Demand.Enums;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Enums;

namespace HudhudNestApi.Application.Tests.Demand;

public sealed class SearchRequestMatcherTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly SearchRequestMatchingOptions Options = new();
    private static readonly CurrencyRates Rates = new(new Dictionary<string, decimal> { ["USD"] = 1m, ["SYP"] = 13_000m, ["EUR"] = 0.9m });

    private static PropertySearchRequest Request(
        ListingType type = ListingType.ForRent, int? district = null, int? propertyType = null, decimal max = 400m,
        int? minRooms = null, decimal? minArea = null, bool balcony = false, string currency = "USD")
        => PropertySearchRequest.Create(Guid.NewGuid(), type, 1, district, null, propertyType, null, max, currency,
            minRooms, minArea, null, balcony, false, false, false, null, null, Now);

    private static ListingFacts Listing(
        ListingType type = ListingType.ForRent, int gov = 1, int? district = null, int? propertyType = null,
        decimal? rent = 350m, decimal? sale = null, string currency = "USD", int? rooms = 3, decimal? area = 100m, bool balcony = false)
        => new(Guid.NewGuid(), Guid.NewGuid(), type, gov, district, null, propertyType, sale, rent, currency, rooms, area,
            balcony, false, false, FurnishingStatus.Unfurnished);

    [Fact]
    public void Hard_rules_exclude_type_governorate_property_type_budget_rooms()
    {
        Assert.Null(SearchRequestMatcher.Evaluate(Request(), Listing(type: ListingType.ForSale, sale: 1m), Rates, Options));
        Assert.Null(SearchRequestMatcher.Evaluate(Request(), Listing(gov: 2), Rates, Options));
        Assert.Null(SearchRequestMatcher.Evaluate(Request(propertyType: 5), Listing(propertyType: 6), Rates, Options));
        Assert.Null(SearchRequestMatcher.Evaluate(Request(max: 300m), Listing(rent: 320m), Rates, Options)); // > 5 % above
        Assert.Null(SearchRequestMatcher.Evaluate(Request(minRooms: 3), Listing(rooms: 2), Rates, Options));
        Assert.Null(SearchRequestMatcher.Evaluate(Request(minArea: 120m), Listing(area: 100m), Rates, Options));
    }

    [Fact]
    public void A_listing_without_a_price_or_with_an_unconvertible_currency_is_not_a_match()
    {
        Assert.Null(SearchRequestMatcher.Evaluate(Request(), Listing(rent: null), Rates, Options));
        Assert.Null(SearchRequestMatcher.Evaluate(Request(), Listing(currency: "TRY"), Rates, Options));
    }

    [Fact]
    public void Currencies_are_converted_before_the_budget_check()
    {
        // 4,550,000 SYP = 350 USD at 13,000 per dollar.
        Assert.NotNull(SearchRequestMatcher.Evaluate(Request(max: 400m), Listing(rent: 4_550_000m, currency: "SYP"), Rates, Options));
        Assert.Null(SearchRequestMatcher.Evaluate(Request(max: 300m), Listing(rent: 4_550_000m, currency: "SYP"), Rates, Options));
    }

    [Fact]
    public void Rent_and_sale_listing_is_priced_by_the_requested_side()
    {
        var both = Listing(type: ListingType.ForRentAndSale, rent: 300m, sale: 90_000m);
        Assert.NotNull(SearchRequestMatcher.Evaluate(Request(type: ListingType.ForRent, max: 400m), both, Rates, Options));
        Assert.Null(SearchRequestMatcher.Evaluate(Request(type: ListingType.ForSale, max: 50_000m), both, Rates, Options));
    }

    [Fact]
    public void Unknown_specs_earn_nothing_and_are_named()
    {
        var known = SearchRequestMatcher.Evaluate(Request(minRooms: 2, balcony: true), Listing(rooms: 3, balcony: true), Rates, Options)!;
        var unknown = SearchRequestMatcher.Evaluate(Request(minRooms: 2, balcony: true), Listing(rooms: null, balcony: false), Rates, Options)!;

        Assert.Equal(100, known.Score);
        Assert.True(unknown.Score < known.Score);
        Assert.Contains("rooms_unknown", unknown.Reasons);
        Assert.Contains("balcony_unknown", unknown.Reasons);
        Assert.DoesNotContain("rooms_fit", unknown.Reasons);
    }

    [Fact]
    public void Slightly_above_budget_scores_lower_and_says_so()
    {
        var result = SearchRequestMatcher.Evaluate(Request(max: 400m), Listing(rent: 410m), Rates, Options)!;
        Assert.Contains("budget_slightly_above", result.Reasons);
        Assert.True(result.Score < 100);
    }

    [Fact]
    public void A_different_district_still_matches_with_fewer_points()
    {
        var same = SearchRequestMatcher.Evaluate(Request(district: 7), Listing(district: 7), Rates, Options)!;
        var other = SearchRequestMatcher.Evaluate(Request(district: 7), Listing(district: 8), Rates, Options)!;
        Assert.True(same.Score > other.Score);
        Assert.Contains("location_governorate", other.Reasons);
    }

    [Theory]
    [InlineData("اتصل 0944123456", true)]
    [InlineData("call +963 944 123 456", true)]
    [InlineData("٠٩٤٤١٢٣٤٥٦", true)]
    [InlineData("a@b.de", true)]
    [InlineData("see https://x.y", true)]
    [InlineData("واتس فقط", true)]
    [InlineData("قرب الجامعة، طابق ثالث، 3 غرف", false)]
    [InlineData("budget 2026 or 2027", false)]
    public void Contact_details_are_detected(string text, bool expected)
        => Assert.Equal(expected, SearchRequestInputRules.ContainsContactDetails(text));
}

public sealed class PropertySearchRequestLifecycleTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static PropertySearchRequest Make(DateTime? expires = null)
        => PropertySearchRequest.Create(Guid.NewGuid(), ListingType.ForSale, 1, null, null, null, null, 100_000m, "USD",
            null, null, null, false, false, false, false, null, expires, Now);

    [Fact]
    public void Expiry_defaults_to_sixty_days_and_is_capped_at_ninety()
    {
        Assert.Equal(Now.AddDays(60), Make().ExpiresAt);
        Assert.Equal(Now.AddDays(90), Make(Now.AddDays(400)).ExpiresAt);
        Assert.Throws<DomainException>(() => Make(Now.AddDays(-1)));
    }

    [Fact]
    public void Pause_resume_close_and_expire_follow_the_rules()
    {
        var r = Make();
        r.Pause(Now);
        Assert.Equal(SearchRequestStatus.Paused, r.Status);
        Assert.False(r.IsActive(Now));
        r.Resume(Now);
        Assert.True(r.IsActive(Now));
        Assert.Null(r.MatchedUpTo);

        Assert.True(r.ExpireIfDue(Now.AddDays(61)));
        Assert.Throws<InvalidStateTransitionException>(() => r.Pause(Now));
        r.Renew(null, Now.AddDays(61));
        Assert.Equal(SearchRequestStatus.Active, r.Status);

        r.Close(Now);
        Assert.Throws<InvalidStateTransitionException>(() => r.Resume(Now));
        Assert.Throws<InvalidStateTransitionException>(() => r.Renew(null, Now));
    }

    [Fact]
    public void Rent_and_sale_is_not_a_request_type()
        => Assert.Throws<DomainException>(() => PropertySearchRequest.Create(Guid.NewGuid(), ListingType.ForRentAndSale, 1, null, null, null,
            null, 1m, "USD", null, null, null, false, false, false, false, null, null, Now));
}
