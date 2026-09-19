using PropertyApi.Application.ShortStay.Commands.CreateShortStayListing;
using PropertyApi.Application.ShortStay.Commands.SetPricingRules;
using PropertyApi.Application.ShortStay.Commands.UpdateShortStayListing;
using PropertyApi.Application.ShortStay.DTOs;
using PropertyApi.Application.ShortStay.Queries.GetUnitAvailability;
using PropertyApi.Application.ShortStay.Queries.SearchShortStayListings;

namespace PropertyApi.Application.Tests.ShortStay;

/// <summary>
/// Regression coverage for the 2026-09-18 audit findings: unbounded pageSize, unguarded
/// Enum.Parse on external input (was an opaque 500), and unbounded availability span.
/// </summary>
public sealed class ShortStayHardeningValidatorTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(100, true)]
    [InlineData(101, false)]
    [InlineData(int.MaxValue, false)]
    public void Search_PageSize_IsBoundedTo1Through100(int pageSize, bool valid)
    {
        var query = new SearchShortStayListingsQuery(new ShortStayListingSearchFilter { PageSize = pageSize });

        Assert.Equal(valid, new SearchShortStayListingsQueryValidator().Validate(query).IsValid);
    }

    private static CreateShortStayListingCommand ValidCreate(string currency) => new(
        OwnerId: Guid.NewGuid(), AccommodationTypeId: 1, Title: "t", Description: "d", Capacity: 2,
        Bedrooms: 1, Bathrooms: 1, CheckInTime: new TimeOnly(14, 0), CheckOutTime: new TimeOnly(11, 0),
        Latitude: 34m, Longitude: 35m, DefaultBasePricePerNight: 50m, PropertyId: null, CurrencyCode: currency);

    [Theory]
    [InlineData("USD", true)]
    [InlineData("syp", true)]
    [InlineData("", false)]
    [InlineData("XXX", false)]
    [InlineData("DOLLAR", false)]
    public void Create_CurrencyCode_MustBeSupported(string currency, bool valid)
    {
        Assert.Equal(valid, new CreateShortStayListingCommandValidator().Validate(ValidCreate(currency)).IsValid);
    }

    private static UpdateShortStayListingCommand ValidUpdate(
        string visibility = "Exact", string? poolType = null, string? poolLocation = null, decimal? deposit = null) => new(
        ListingId: Guid.NewGuid(), OwnerId: Guid.NewGuid(), Title: "t", Description: "d",
        Capacity: 2, Bedrooms: 1, Bathrooms: 1,
        CheckInTime: new TimeOnly(14, 0), CheckOutTime: new TimeOnly(11, 0),
        SelfCheckInEnabled: false, InstantBookingEnabled: true, RequestBookingEnabled: false,
        Latitude: 35m, Longitude: 35m, GovernorateId: null, DistrictId: null, NeighborhoodId: null, City: null,
        LocationVisibility: visibility, PoolType: poolType, PoolLocation: poolLocation,
        PoolIsSeasonal: null, PoolIsHeated: null, CleaningFee: 0, ExtraGuestFee: 0, ExtraBedFee: 0,
        AllowsSmoking: false, AllowsParties: false, AllowsPets: false,
        QuietHoursStart: null, QuietHoursEnd: null, CustomRulesText: null,
        CancellationFreeCancellationDays: 3, CancellationDepositRefundable: true,
        CancellationCustomTermsText: null, DepositPercentage: deposit);

    [Fact]
    public void Update_ValidCommand_Passes()
        => Assert.True(new UpdateShortStayListingCommandValidator()
            .Validate(ValidUpdate(poolType: "Private", poolLocation: "Indoor", deposit: 20m)).IsValid);

    [Theory]
    [InlineData("bogus", null, null)]
    [InlineData("Exact", "bogus", "Indoor")]
    [InlineData("Exact", "Private", "bogus")]
    public void Update_UnknownEnumStrings_AreRejectedAsValidationErrorsNotExceptions(
        string visibility, string? poolType, string? poolLocation)
        => Assert.False(new UpdateShortStayListingCommandValidator()
            .Validate(ValidUpdate(visibility, poolType, poolLocation)).IsValid);

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Update_DepositPercentageOutOfRange_IsRejected(int deposit)
        => Assert.False(new UpdateShortStayListingCommandValidator()
            .Validate(ValidUpdate(deposit: deposit)).IsValid);

    [Fact]
    public void SetPricingRules_UnknownRuleType_IsRejected()
    {
        var command = new SetPricingRulesCommand(Guid.NewGuid(), Guid.NewGuid(),
            [new PricingRuleInput("notarealtype", null, null, null, 50m)]);

        Assert.False(new SetPricingRulesCommandValidator().Validate(command).IsValid);
    }

    [Fact]
    public void SetPricingRules_KnownRuleType_Passes()
    {
        var command = new SetPricingRulesCommand(Guid.NewGuid(), Guid.NewGuid(),
            [new PricingRuleInput("Weekend", null, null, null, 50m)]);

        Assert.True(new SetPricingRulesCommandValidator().Validate(command).IsValid);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(366, true)]
    [InlineData(367, false)]
    [InlineData(-1, false)]
    public void Availability_SpanIsBounded(int spanDays, bool valid)
    {
        var from = new DateOnly(2026, 1, 1);
        var query = new GetUnitAvailabilityQuery(Guid.NewGuid(), from, from.AddDays(spanDays));

        Assert.Equal(valid, new GetUnitAvailabilityQueryValidator().Validate(query).IsValid);
    }

    [Fact]
    public void Availability_FullDateRange_IsRejected()
    {
        var query = new GetUnitAvailabilityQuery(Guid.NewGuid(), DateOnly.MinValue, DateOnly.MaxValue);

        Assert.False(new GetUnitAvailabilityQueryValidator().Validate(query).IsValid);
    }
}
