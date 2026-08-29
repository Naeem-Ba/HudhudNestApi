using PropertyApi.Application.ShortStay.Services;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.ShortStay.Entities;
using PropertyApi.Domain.ShortStay.Enums;

namespace PropertyApi.Application.Tests.ShortStay.Services;

public sealed class PricingCalculationServiceTests
{
    private readonly PricingCalculationService _sut = new();
    private static readonly DateOnly Base = new(2026, 8, 1); // a Saturday

    private static RoomType MakeRoomType(decimal basePrice = 80m, int? capacity = null) =>
        new() { Name = "Default", BasePricePerNight = basePrice, Capacity = capacity };

    [Fact]
    public void Calculate_NoRules_UsesRoomTypeBasePriceForEveryNight()
    {
        var roomType = MakeRoomType(100m);

        var result = _sut.Calculate(
            roomType, [], [], listingCleaningFee: 0m, listingExtraGuestFee: 0m, listingExtraBedFee: 0m,
            listingCapacity: 4, checkIn: Base, checkOut: Base.AddDays(3), countedGuests: 2);

        Assert.Equal(3, result.Nights.Count);
        Assert.All(result.Nights, n => Assert.Equal(100m, n.Price));
        Assert.Equal(300m, result.NightsSubtotal);
        Assert.Equal(300m, result.TotalAmount);
    }

    [Fact]
    public void Calculate_ScenarioB_WeekendPricingAppliedPerNight()
    {
        // Thursday=80, Friday/Saturday(weekend)=120 — mirrors plan's Scenario B.
        var roomType = MakeRoomType(80m);
        var thursday = new DateOnly(2026, 8, 6); // Thursday
        var pricingRules = new List<PricingRule>
        {
            new() { RoomTypeId = roomType.Id, RuleType = PricingRuleType.Weekend, DayOfWeek = DayOfWeek.Friday, PricePerNight = 120m },
            new() { RoomTypeId = roomType.Id, RuleType = PricingRuleType.Weekend, DayOfWeek = DayOfWeek.Saturday, PricePerNight = 120m },
        };

        var result = _sut.Calculate(
            roomType, pricingRules, [], 0m, 0m, 0m, 4, thursday, thursday.AddDays(3), 2);

        Assert.Equal(80m, result.Nights[0].Price);  // Thursday: no rule -> base
        Assert.Equal(120m, result.Nights[1].Price); // Friday: weekend
        Assert.Equal(120m, result.Nights[2].Price); // Saturday: weekend
        Assert.Equal(320m, result.NightsSubtotal);
    }

    [Fact]
    public void Calculate_PriorityOrder_CustomDateBeatsHolidayBeatsSeasonalBeatsWeekendBeatsWeekday()
    {
        var roomType = MakeRoomType(50m);
        var date = new DateOnly(2026, 12, 25); // Friday
        var rules = new List<PricingRule>
        {
            new() { RoomTypeId = roomType.Id, RuleType = PricingRuleType.Weekday, DayOfWeek = date.DayOfWeek, PricePerNight = 60m },
            new() { RoomTypeId = roomType.Id, RuleType = PricingRuleType.Weekend, DayOfWeek = date.DayOfWeek, PricePerNight = 70m },
            new() { RoomTypeId = roomType.Id, RuleType = PricingRuleType.Seasonal, DateRangeStart = date.AddDays(-5), DateRangeEnd = date.AddDays(5), PricePerNight = 90m },
            new() { RoomTypeId = roomType.Id, RuleType = PricingRuleType.Holiday, DateRangeStart = date, DateRangeEnd = date, PricePerNight = 150m },
            new() { RoomTypeId = roomType.Id, RuleType = PricingRuleType.CustomDate, DateRangeStart = date, DateRangeEnd = date, PricePerNight = 200m },
        };

        var result = _sut.Calculate(roomType, rules, [], 0m, 0m, 0m, 4, date, date.AddDays(1), 2);

        Assert.Equal(200m, result.Nights.Single().Price);
        Assert.Equal(nameof(PricingRuleType.CustomDate), result.Nights.Single().AppliedRuleType);
    }

    [Fact]
    public void Calculate_ExtraGuestFee_ChargedOnlyForGuestsBeyondCapacity_PerNight()
    {
        var roomType = MakeRoomType(100m, capacity: 2);

        var result = _sut.Calculate(
            roomType, [], [], listingCleaningFee: 20m, listingExtraGuestFee: 15m, listingExtraBedFee: 0m,
            listingCapacity: 2, checkIn: Base, checkOut: Base.AddDays(2), countedGuests: 4);

        // 2 nights * (4-2 excess guests) * 15 = 60
        Assert.Equal(60m, result.ExtraGuestFee);
        Assert.Equal(20m, result.CleaningFee);
        Assert.Equal(200m + 20m + 60m, result.TotalAmount);
    }

    [Fact]
    public void Calculate_MinimumStay_DefaultRule_ThrowsWhenStayTooShort()
    {
        var roomType = MakeRoomType();
        var minStayRules = new List<MinimumStayRule> { new() { RoomTypeId = roomType.Id, MinimumNights = 2 } };

        Assert.Throws<DomainException>(() => _sut.Calculate(
            roomType, [], minStayRules, 0m, 0m, 0m, 4, Base, Base.AddDays(1), 2));
    }

    [Fact]
    public void Calculate_MinimumStay_SeasonalOverride_TakesPrecedenceOverDefault()
    {
        // Scenario A/B analog: default = 1 night, summer season = 3 nights minimum.
        var roomType = MakeRoomType();
        var summerStart = new DateOnly(2026, 7, 1);
        var summerEnd = new DateOnly(2026, 8, 31);
        var minStayRules = new List<MinimumStayRule>
        {
            new() { RoomTypeId = roomType.Id, MinimumNights = 1 },
            new() { RoomTypeId = roomType.Id, MinimumNights = 3, DateRangeStart = summerStart, DateRangeEnd = summerEnd },
        };

        // 2 nights in August should fail (needs 3).
        Assert.Throws<DomainException>(() => _sut.Calculate(
            roomType, [], minStayRules, 0m, 0m, 0m, 4, Base, Base.AddDays(2), 2));

        // 3 nights in August should succeed.
        var result = _sut.Calculate(roomType, [], minStayRules, 0m, 0m, 0m, 4, Base, Base.AddDays(3), 2);
        Assert.Equal(3, result.MinimumNightsRequired);
    }

    [Fact]
    public void Calculate_ScenarioA_Chalet_RespectsTwoNightMinimum()
    {
        // 4 guests, $100/night, minimum 2 nights: 10->12 Aug succeeds, 10->11 Aug is rejected.
        var roomType = MakeRoomType(100m);
        var minStayRules = new List<MinimumStayRule> { new() { RoomTypeId = roomType.Id, MinimumNights = 2 } };
        var aug10 = new DateOnly(2026, 8, 10);

        var ok = _sut.Calculate(roomType, [], minStayRules, 0m, 0m, 0m, 4, aug10, aug10.AddDays(2), 4);
        Assert.Equal(200m, ok.TotalAmount);

        Assert.Throws<DomainException>(() => _sut.Calculate(
            roomType, [], minStayRules, 0m, 0m, 0m, 4, aug10, aug10.AddDays(1), 4));
    }

    [Fact]
    public void Calculate_Throws_WhenCheckOutNotAfterCheckIn()
    {
        var roomType = MakeRoomType();
        Assert.Throws<DomainException>(() => _sut.Calculate(
            roomType, [], [], 0m, 0m, 0m, 4, Base, Base, 2));
    }
}
