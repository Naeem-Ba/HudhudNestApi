using HudhudNestApi.Application.Listings.Currencies;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Tests.Currencies;

/// <summary>
/// Cross-currency price comparison (QA Phase 7, P7-D01), evaluated in memory. The same expression
/// trees are translated to SQL by EF Core; PropertyCurrencySearchPostgresTests runs them there.
/// </summary>
public sealed class CurrencyRatesAndPriceExpressionsTests
{
    // Units per USD: 1 USD = 1, 0.9 EUR, 10,000 SYP.
    private static readonly CurrencyRates Rates = new(new Dictionary<string, decimal>
    {
        ["USD"] = 1m,
        ["EUR"] = 0.9m,
        ["SYP"] = 10_000m
    });

    private static Property Sale(string currency, decimal? price)
    {
        var p = Property.Create("t", "description long enough", Guid.NewGuid(), ListingType.ForSale, currencyCode: currency);
        p.PurchasePrice = price;
        return p;
    }

    private static Property Rent(string currency, decimal? cold, decimal? warm = null)
    {
        var p = Property.Create("t", "description long enough", Guid.NewGuid(), ListingType.ForRent, currencyCode: currency);
        p.ColdRent = cold;
        p.WarmRent = warm;
        return p;
    }

    [Fact]
    public void Convert_UsesTheRateTable()
    {
        Assert.Equal(100m, Rates.Convert(100m, "usd", "USD"));
        Assert.Equal(90m, Rates.Convert(100m, "USD", "EUR"));
        Assert.Equal(1_000_000m, Rates.Convert(100m, "USD", "SYP"));
        Assert.Equal(10m, Rates.Convert(100_000m, "SYP", "USD"));
        Assert.Null(Rates.Convert(100m, "USD", "GBP"));
    }

    [Fact(DisplayName = "Codes with trailing blanks (char(5) column) and non-positive rates are normalised away")]
    public void Constructor_NormalisesCodes_AndDropsBadRates()
    {
        var rates = new CurrencyRates(new Dictionary<string, decimal> { ["usd  "] = 1m, ["BAD"] = 0m, [" "] = 5m });

        Assert.True(rates.TryGetRate("USD", out _));
        Assert.False(rates.TryGetRate("BAD", out _));
        Assert.Single(rates.UnitsPerUsd);
    }

    [Fact(DisplayName = "A 100,000 USD ceiling keeps a 90,000 EUR flat but not a 2,000,000,000 SYP one")]
    public void MaxPrice_ComparesAcrossCurrencies()
    {
        var range = ListingPriceExpressions.PriceRange(ListingType.ForSale, null, 100_000m, "USD", Rates)!.Compile();

        Assert.True(range(Sale("USD", 99_000m)));
        Assert.False(range(Sale("USD", 101_000m)));
        Assert.True(range(Sale("EUR", 90_000m)));     // = 100,000 USD
        Assert.False(range(Sale("EUR", 91_000m)));    // > 100,000 USD
        Assert.True(range(Sale("SYP", 900_000_000m)));    // = 90,000 USD
        Assert.False(range(Sale("SYP", 2_000_000_000m))); // = 200,000 USD
    }

    [Fact]
    public void MinAndMax_FormARangeInTheChosenCurrency()
    {
        var range = ListingPriceExpressions.PriceRange(ListingType.ForSale, 50_000m, 100_000m, "USD", Rates)!.Compile();

        Assert.False(range(Sale("SYP", 400_000_000m)));  // 40,000 USD
        Assert.True(range(Sale("SYP", 700_000_000m)));   // 70,000 USD
        Assert.False(range(Sale("EUR", 40_000m)));       // ~44,444 USD
        Assert.True(range(Sale("EUR", 60_000m)));        // ~66,667 USD
    }

    [Fact(DisplayName = "A range written in SYP is converted the other way round")]
    public void RangeInLira_MatchesDollarListings()
    {
        var range = ListingPriceExpressions.PriceRange(ListingType.ForSale, 500_000_000m, 1_500_000_000m, "SYP", Rates)!.Compile();

        Assert.True(range(Sale("USD", 100_000m)));   // 1,000,000,000 SYP
        Assert.False(range(Sale("USD", 200_000m)));  // 2,000,000,000 SYP
        Assert.True(range(Sale("SYP", 1_000_000_000m)));
    }

    [Fact(DisplayName = "A currency without a rate matches only when it is the chosen currency")]
    public void UnpricedCurrency_MatchesOnlyItself()
    {
        var inUsd = ListingPriceExpressions.PriceRange(ListingType.ForSale, null, 100_000m, "USD", Rates)!.Compile();
        Assert.False(inUsd(Sale("GBP", 10m)));   // cannot be compared, so it is not claimed to fit

        var inGbp = ListingPriceExpressions.PriceRange(ListingType.ForSale, null, 100m, "GBP", Rates)!.Compile();
        Assert.True(inGbp(Sale("GBP", 90m)));
        Assert.False(inGbp(Sale("GBP", 110m)));
        Assert.False(inGbp(Sale("USD", 1m)));    // GBP has no rate, so USD cannot be converted into it
    }

    [Fact(DisplayName = "The established field rules hold: a minimum is met by any price field, a maximum by all of them")]
    public void RentFieldRules_ArePreserved()
    {
        var min = ListingPriceExpressions.PriceRange(ListingType.ForRent, 300m, null, "USD", Rates)!.Compile();
        Assert.True(min(Rent("USD", 100m, 350m)));
        Assert.False(min(Rent("USD", 100m, 200m)));

        var max = ListingPriceExpressions.PriceRange(ListingType.ForRent, null, 300m, "USD", Rates)!.Compile();
        Assert.True(max(Rent("USD", 200m, null)));
        Assert.False(max(Rent("USD", 200m, 400m)));
    }

    [Fact]
    public void NoBounds_ProducesNoPredicate()
    {
        Assert.Null(ListingPriceExpressions.PriceRange(ListingType.ForSale, null, null, "USD", Rates));
    }

    [Fact(DisplayName = "The price used for ordering is the sale price for sale and cold-then-warm rent for rent")]
    public void EffectivePrice_ByListingType()
    {
        var sale = ListingPriceExpressions.EffectivePrice(ListingType.ForSale).Compile();
        var rent = ListingPriceExpressions.EffectivePrice(ListingType.ForRent).Compile();

        Assert.Equal(500m, sale(Sale("USD", 500m)));
        Assert.Equal(40m, rent(Rent("USD", null, 40m)));
        Assert.Equal(30m, rent(Rent("USD", 30m, 40m)));
        Assert.Null(sale(Rent("USD", 30m)));
    }

    [Fact(DisplayName = "Converted to dollars for ordering; no price and unpriced currencies yield null")]
    public void EffectivePriceInUsd_ConvertsAndNullsTheUnknown()
    {
        var usd = ListingPriceExpressions.EffectivePriceInUsd(ListingType.ForSale, Rates).Compile();

        Assert.Equal(100m, usd(Sale("USD", 100m)));
        Assert.Equal(100m, usd(Sale("SYP", 1_000_000m)));
        Assert.Equal(100m, usd(Sale("EUR", 90m)));
        Assert.Null(usd(Sale("USD", null)));
        Assert.Null(usd(Sale("GBP", 100m)));
    }

    [Fact(DisplayName = "With no rates on file, ordering falls back to the numbers as written")]
    public void EffectivePriceInUsd_WithoutRates_UsesRawPrice()
    {
        var raw = ListingPriceExpressions.EffectivePriceInUsd(ListingType.ForSale, CurrencyRates.Empty).Compile();

        Assert.Equal(123m, raw(Sale("USD", 123m)));
    }
}
