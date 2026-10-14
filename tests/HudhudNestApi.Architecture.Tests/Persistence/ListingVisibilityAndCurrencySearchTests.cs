using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Listings.Currencies;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Repositories;

namespace HudhudNestApi.Architecture.Tests.Persistence;

/// <summary>
/// The public search rules that QA Phase 7 changed: which statuses are visible, the cross-currency
/// price range and the price ordering. Rules run over LINQ-to-Objects (the translation to SQL is
/// proved against a real PostgreSQL by PropertyCurrencySearchPostgresTests in the integration suite);
/// the SQL-shape facts here only assert that user numbers travel as parameters.
/// </summary>
[Trait("Category", "Persistence")]
public sealed class ListingVisibilityAndCurrencySearchTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CurrencyRates Rates = new(new Dictionary<string, decimal>
    {
        ["USD"] = 1m,
        ["EUR"] = 0.9m,
        ["SYP"] = 10_000m
    });

    private static Property Listing(
        string title, PropertyStatus status = PropertyStatus.Available, Guid? agencyId = null,
        ListingType type = ListingType.ForSale, string currency = "USD", decimal? price = null, bool published = true)
    {
        var p = Property.Create(title, $"{title} description", Guid.NewGuid(), type, currencyCode: currency, isPublished: published);
        p.Status = status;
        if (type == ListingType.ForRent) p.ColdRent = price; else p.PurchasePrice = price;
        typeof(Property).GetProperty(nameof(Property.AgencyId))!.SetValue(p, agencyId);
        return p;
    }

    private static string[] Titles(IEnumerable<Property> listings, PropertyFilterDto filter)
        => PropertyRepository.ApplyFilter(listings.AsQueryable(), filter, null, Rates).Select(p => p.Title).OrderBy(t => t).ToArray();

    [Fact(DisplayName = "A plain search lists Available and Reserved only: Archived, Sold and Rented never appear")]
    public void DefaultSearch_HidesArchivedSoldAndRented()
    {
        var all = new[]
        {
            Listing("available"), Listing("reserved", PropertyStatus.Reserved), Listing("archived", PropertyStatus.Archived),
            Listing("sold", PropertyStatus.Sold), Listing("rented", PropertyStatus.Rented), Listing("expired", PropertyStatus.Expired)
        };

        Assert.Equal(["available", "reserved"], Titles(all, new PropertyFilterDto()));
    }

    [Fact(DisplayName = "An explicit Sold filter without an agency still finds nothing public")]
    public void StatusFilter_WithoutAgency_DoesNotExposeClosedDeals()
    {
        var all = new[] { Listing("sold", PropertyStatus.Sold), Listing("available") };

        Assert.Empty(Titles(all, new PropertyFilterDto { Status = PropertyStatus.Sold }));
    }

    [Fact(DisplayName = "The agency page serves Sold and Rented under their own status filter, and only that agency's")]
    public void AgencyTabs_ShowClosedDeals_OfThatAgencyOnly()
    {
        var agency = Guid.NewGuid();
        var other = Guid.NewGuid();
        var all = new[]
        {
            Listing("sold-mine", PropertyStatus.Sold, agency), Listing("rented-mine", PropertyStatus.Rented, agency),
            Listing("sold-other", PropertyStatus.Sold, other), Listing("available-mine", agencyId: agency),
            Listing("sold-unpublished", PropertyStatus.Sold, agency, published: false)
        };

        Assert.Equal(["sold-mine"], Titles(all, new PropertyFilterDto { AgencyId = agency, Status = PropertyStatus.Sold }));
        Assert.Equal(["rented-mine"], Titles(all, new PropertyFilterDto { AgencyId = agency, Status = PropertyStatus.Rented }));
        Assert.Equal(["available-mine"], Titles(all, new PropertyFilterDto { AgencyId = agency }));
        Assert.Empty(Titles(all, new PropertyFilterDto { AgencyId = agency, Status = PropertyStatus.Archived }));
    }

    [Fact(DisplayName = "With a currency, the price range matches equivalent prices in other currencies")]
    public void PriceRange_WithCurrency_MatchesEquivalentPrices()
    {
        var all = new[]
        {
            Listing("usd-ok", currency: "USD", price: 90_000m),
            Listing("eur-ok", currency: "EUR", price: 80_000m),        // ~88,888 USD
            Listing("syp-ok", currency: "SYP", price: 800_000_000m),    // 80,000 USD
            Listing("eur-high", currency: "EUR", price: 95_000m),       // ~105,555 USD
            Listing("syp-high", currency: "SYP", price: 2_000_000_000m) // 200,000 USD
        };

        var filter = new PropertyFilterDto { ListingType = ListingType.ForSale, CurrencyCode = "usd", MaxPrice = 100_000m };

        Assert.Equal(["eur-ok", "syp-ok", "usd-ok"], Titles(all, filter));
    }

    [Fact(DisplayName = "Without a currency the numbers are compared as written, as older clients expect")]
    public void PriceRange_WithoutCurrency_IsTheLegacyRawComparison()
    {
        var all = new[]
        {
            Listing("usd", currency: "USD", price: 90_000m),
            Listing("syp", currency: "SYP", price: 2_000_000_000m)
        };

        Assert.Equal(["usd"], Titles(all, new PropertyFilterDto { ListingType = ListingType.ForSale, MaxPrice = 100_000m }));
    }

    private static string[] SortedTitles(Property[] all, bool descending, ListingType type = ListingType.ForSale)
        => PropertyRepository.ApplySort(all.AsQueryable(),
                new PropertyFilterDto { SortBy = "Price", ListingType = type, SortDescending = descending }, Now, Rates)
            .Select(p => p.Title).ToArray();

    [Fact(DisplayName = "Ordering by price compares dollars, not raw numbers, and puts listings without a price last either way")]
    public void PriceSort_AcrossCurrencies_NoPriceLast()
    {
        var all = new[]
        {
            Listing("syp-70k", currency: "SYP", price: 700_000_000m),  // 70,000 USD
            Listing("usd-50k", currency: "USD", price: 50_000m),
            Listing("eur-100k", currency: "EUR", price: 90_000m),      // 100,000 USD
            Listing("no-price", price: null),
            Listing("gbp-unknown", currency: "GBP", price: 1m)          // no rate: cannot be placed
        };

        var ascending = SortedTitles(all, descending: false);
        var descending = SortedTitles(all, descending: true);

        Assert.Equal(["usd-50k", "syp-70k", "eur-100k"], ascending[..3]);
        Assert.Equal(["eur-100k", "syp-70k", "usd-50k"], descending[..3]);
        Assert.Equal(["gbp-unknown", "no-price"], ascending[3..].Order().ToArray());
        Assert.Equal(["gbp-unknown", "no-price"], descending[3..].Order().ToArray());
    }

    [Fact(DisplayName = "Rent ordering uses cold rent, falling back to warm rent, never the sale price")]
    public void PriceSort_ForRent_UsesRent()
    {
        var cold = Listing("cold-200", type: ListingType.ForRent, price: 200m);
        var warm = Listing("warm-100", type: ListingType.ForRent);
        warm.WarmRent = 100m;
        var sale = Listing("sale-1", type: ListingType.ForRent);
        sale.PurchasePrice = 1m;

        Assert.Equal(["warm-100", "cold-200", "sale-1"], SortedTitles([cold, warm, sale], false, ListingType.ForRent));
    }

    [Fact(DisplayName = "Numbers typed into the price range travel as SQL parameters, so EF caches one query per shape (no per-value cache growth)")]
    public void PriceRange_NumbersAreParameters_NotLiterals()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=sql_shape_only;Username=none;Password=none").Options;
        using var db = new AppDbContext(options);

        string Sql(decimal min, decimal max) => SqlBody(PropertyRepository.ApplyFilter(
            db.Properties,
            new PropertyFilterDto { ListingType = ListingType.ForSale, CurrencyCode = "USD", MinPrice = min, MaxPrice = max },
            null, Rates).ToQueryString());

        var first = Sql(1_000m, 2_000m);
        var second = Sql(31_337m, 99_999m);

        Assert.Equal(first, second);
        Assert.DoesNotContain("31337", second);
        Assert.DoesNotContain("99999", second);
    }

    [Fact]
    public void PriceSort_SqlOrdersNoPriceLast()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=sql_shape_only;Username=none;Password=none").Options;
        using var db = new AppDbContext(options);

        var sql = PropertyRepository.ApplySort(db.Properties,
            new PropertyFilterDto { SortBy = "Price", ListingType = ListingType.ForSale, SortDescending = true }, Now, Rates).ToQueryString();
        var orderBy = sql[sql.LastIndexOf("ORDER BY", StringComparison.Ordinal)..];

        // First key is "has no price" ascending (false first), second the dollar value in the requested direction.
        Assert.Matches(@"ORDER BY .* IS NULL.*,.* DESC", orderBy.Replace("\n", " "));
    }

    private static string SqlBody(string queryString)
        => string.Join('\n', queryString.Split('\n').Where(line => !line.StartsWith("--")));
}
