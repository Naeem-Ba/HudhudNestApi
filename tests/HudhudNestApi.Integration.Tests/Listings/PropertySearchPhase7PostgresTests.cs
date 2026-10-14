using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Listings.Currencies;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Persistence.Seeds;
using HudhudNestApi.Infrastructure.Repositories;
using HudhudNestApi.Integration.Tests.Auth;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// QA Phase 7 against a real PostgreSQL: the cross-currency price range and ordering, keyword
/// search over the generated SearchText column, status visibility and the geo-search rules.
/// Everything is seeded under a unique marker so the suite can share a database with others.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class PropertySearchPhase7PostgresTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();
    private readonly string _marker = $"p7{Guid.NewGuid():N}";
    private Guid _ownerId;

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        var owner = await _factory.SeedUserAsync($"{_marker}@test.local");
        _ownerId = owner.UserAccountId;

        await using var scope = _factory.Services.CreateAsyncScope();
        await CurrencySeed.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // -- helpers ---------------------------------------------------------------------------

    private async Task<Guid> AddAsync(
        string suffix, ListingType type = ListingType.ForSale, string currency = "USD", decimal? price = null,
        PropertyStatus status = PropertyStatus.Available, Guid? agencyId = null, string? description = null,
        DateTime? expiresAt = null, decimal? lat = null, decimal? lng = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var property = Property.Create($"{_marker} {suffix}", description ?? "وصف كافٍ للإعلان", _ownerId, type, currencyCode: currency);
        property.Status = status;
        property.ExpiresAt = expiresAt;
        if (type == ListingType.ForRent) property.ColdRent = price; else property.PurchasePrice = price;
        property.SetAgency(agencyId);

        if (lat is { } la && lng is { } lo) { property.Latitude = la; property.Longitude = lo; }
        db.Properties.Add(property);
        await db.SaveChangesAsync();
        return property.Id;
    }

    private async Task<Guid> NewAgencyAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var key = Guid.NewGuid().ToString("N");
        var agency = HudhudNestApi.Domain.Agencies.Entities.Agency.Create(
            $"Agency {key[..8]}", $"ag-{key}", _ownerId, "SY", DateTime.UtcNow);
        db.Agencies.Add(agency);
        await db.SaveChangesAsync();
        return agency.Id;
    }

    private async Task<PagedResult<Property>> ListAsync(PropertyFilterDto filter)
    {
        filter.SearchTerm = string.IsNullOrWhiteSpace(filter.SearchTerm) ? _marker : $"{_marker} {filter.SearchTerm}";
        filter.PageSize = 100;
        await using var scope = _factory.Services.CreateAsyncScope();
        return await new PropertyRepository(scope.ServiceProvider.GetRequiredService<AppDbContext>())
            .GetPagedAsync(filter, CancellationToken.None);
    }

    private static string[] Names(PagedResult<Property> page, bool ordered = false)
    {
        var names = page.Items.Select(p => p.Title[(p.Title.IndexOf(' ') + 1)..]);
        return (ordered ? names : names.OrderBy(n => n, StringComparer.Ordinal)).ToArray();
    }

    // -- currency --------------------------------------------------------------------------

    [Fact(DisplayName = "The price range in one currency returns equivalent prices advertised in the others (P7-D01)")]
    public async Task PriceRange_ReturnsEquivalentPricesInOtherCurrencies()
    {
        decimal Rate(string code)
        {
            using var scope = _factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<AppDbContext>().Currencies
                .AsNoTracking().Single(c => c.Code == code).ExchangeRateToUSD;
        }

        // Prices built from the table's own rates so the expectation does not hardcode today's rates.
        await AddAsync("usd-in", currency: "USD", price: 80_000m);
        await AddAsync("eur-in", currency: "EUR", price: 80_000m * Rate("EUR"));     // = 80,000 USD
        await AddAsync("syp-in", currency: "SYP", price: 80_000m * Rate("SYP"));     // = 80,000 USD
        await AddAsync("usd-out", currency: "USD", price: 120_000m);
        await AddAsync("eur-out", currency: "EUR", price: 120_000m * Rate("EUR"));
        await AddAsync("syp-out", currency: "SYP", price: 120_000m * Rate("SYP"));
        await AddAsync("rent-ignored", type: ListingType.ForRent, currency: "USD", price: 100m);

        var inDollars = await ListAsync(new PropertyFilterDto
        {
            ListingType = ListingType.ForSale,
            CurrencyCode = "USD",
            MinPrice = 50_000m,
            MaxPrice = 100_000m
        });
        Assert.Equal(["eur-in", "syp-in", "usd-in"], Names(inDollars));

        // The same budget written in liras returns the same three listings.
        var inLiras = await ListAsync(new PropertyFilterDto
        {
            ListingType = ListingType.ForSale,
            CurrencyCode = "SYP",
            MinPrice = 50_000m * Rate("SYP"),
            MaxPrice = 100_000m * Rate("SYP")
        });
        Assert.Equal(["eur-in", "syp-in", "usd-in"], Names(inLiras));

        // Without a currency: numbers as written (legacy), so only the dollar listings sit under 100,000.
        var legacy = await ListAsync(new PropertyFilterDto { ListingType = ListingType.ForSale, MaxPrice = 100_000m });
        Assert.Equal(["eur-in", "usd-in"], Names(legacy).Where(n => n is "usd-in" or "eur-in" or "syp-in").ToArray());
    }

    [Fact(DisplayName = "Ordering by price puts dollars, euros and liras in true order, listings without a price last, with stable pages")]
    public async Task PriceSort_IsTrueAcrossCurrencies()
    {
        decimal Rate(string code)
        {
            using var scope = _factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<AppDbContext>().Currencies
                .AsNoTracking().Single(c => c.Code == code).ExchangeRateToUSD;
        }

        await AddAsync("c-eur-300", currency: "EUR", price: 300_000m * Rate("EUR"));
        await AddAsync("a-usd-100", currency: "USD", price: 100_000m);
        await AddAsync("b-syp-200", currency: "SYP", price: 200_000m * Rate("SYP"));
        await AddAsync("z-no-price", price: null);

        var ascending = await ListAsync(new PropertyFilterDto { ListingType = ListingType.ForSale, SortBy = "Price", SortDescending = false });
        var descending = await ListAsync(new PropertyFilterDto { ListingType = ListingType.ForSale, SortBy = "Price", SortDescending = true });

        Assert.Equal(["a-usd-100", "b-syp-200", "c-eur-300", "z-no-price"], Names(ascending, ordered: true));
        Assert.Equal(["c-eur-300", "b-syp-200", "a-usd-100", "z-no-price"], Names(descending, ordered: true));
    }

    // -- keyword search --------------------------------------------------------------------

    [Fact(DisplayName = "Search finds Arabic spelling variants, matches every word in any order and treats % and _ literally (P7-D05)")]
    public async Task KeywordSearch_Normalised_MultiWord_AndLiteral()
    {
        await AddAsync("one", description: "شقّة مفروشة قريبة من الجامعة، الطابق ٣");
        await AddAsync("two", description: "Modern flat with big kitchen near the park");
        await AddAsync("three", description: "Discount 100% off, room_1 included");

        Assert.Equal(["one"], Names(await ListAsync(new PropertyFilterDto { SearchTerm = "شقه" })));          // heh for teh marbuta, no vowel mark
        Assert.Equal(["one"], Names(await ListAsync(new PropertyFilterDto { SearchTerm = "مفروشه جامعه" })));
        Assert.Equal(["one"], Names(await ListAsync(new PropertyFilterDto { SearchTerm = "الطابق 3" })));        // ASCII digit finds the Arabic-Indic one
        Assert.Equal(["two"], Names(await ListAsync(new PropertyFilterDto { SearchTerm = "kitchen MODERN" })));   // order and case do not matter
        Assert.Empty(Names(await ListAsync(new PropertyFilterDto { SearchTerm = "modern castle" })));            // every word must match
        Assert.Equal(["three"], Names(await ListAsync(new PropertyFilterDto { SearchTerm = "100%" })));          // not a wildcard
        Assert.Equal(["three"], Names(await ListAsync(new PropertyFilterDto { SearchTerm = "room_1" })));
        Assert.Empty(Names(await ListAsync(new PropertyFilterDto { SearchTerm = "room_2" })));
        Assert.Equal(["three"], Names(await ListAsync(new PropertyFilterDto { SearchTerm = "%" })));            // a lone % is not "match everything"
    }

    [Fact(DisplayName = "PostgreSQL's generated SearchText equals the C# normalisation, so both halves of the rule cannot drift (P7-D05)")]
    public async Task SearchText_MatchesTheCSharpNormaliser()
    {
        const string description = "شقّة مُفروشة أراضي إيجار آمنة مستشفى مسؤول بيئة ـــ ٠١٢٣٤٥٦٧٨٩ ۰۱۲۳ Köln";
        var id = await AddAsync("variants", description: description);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Properties.AsNoTracking().SingleAsync(p => p.Id == id);

        // PostgreSQL does not lower-case (ILIKE does at query time) and the C# side does; whitespace
        // runs are irrelevant to substring matching, so both are collapsed before comparing.
        static string Collapse(string text) => System.Text.RegularExpressions.Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").Trim();
        var expected = SearchTextNormalizer.Normalize($"{row.Title} {row.Description} {row.City} {row.Region} {row.DistrictText} {row.NeighborhoodText}");
        Assert.Equal(Collapse(expected), Collapse(row.SearchText));
        Assert.Contains("شقه مفروشه اراضي ايجار امنه مستشفي مسوول بييه", Collapse(row.SearchText));
        Assert.Contains("0123456789 0123", Collapse(row.SearchText));
    }

    // -- status visibility -----------------------------------------------------------------

    [Fact(DisplayName = "Search and detail follow the agreed status table; agency tabs serve Sold and Rented (P7-D08)")]
    public async Task StatusVisibility_EndToEnd()
    {
        var agency = await NewAgencyAsync();
        var available = await AddAsync("available", agencyId: agency);
        var reserved = await AddAsync("reserved", status: PropertyStatus.Reserved, agencyId: agency);
        var archived = await AddAsync("archived", status: PropertyStatus.Archived, agencyId: agency);
        var sold = await AddAsync("sold", status: PropertyStatus.Sold, agencyId: agency, expiresAt: DateTime.UtcNow.AddYears(-1));
        var rented = await AddAsync("rented", status: PropertyStatus.Rented, agencyId: agency);

        Assert.Equal(["available", "reserved"], Names(await ListAsync(new PropertyFilterDto())));
        Assert.Equal(["available", "reserved"], Names(await ListAsync(new PropertyFilterDto { AgencyId = agency })));
        Assert.Equal(["sold"], Names(await ListAsync(new PropertyFilterDto { AgencyId = agency, Status = PropertyStatus.Sold })));
        Assert.Equal(["rented"], Names(await ListAsync(new PropertyFilterDto { AgencyId = agency, Status = PropertyStatus.Rented })));
        Assert.Empty(Names(await ListAsync(new PropertyFilterDto { Status = PropertyStatus.Sold })));
        Assert.Empty(Names(await ListAsync(new PropertyFilterDto { AgencyId = agency, Status = PropertyStatus.Archived })));

        await using var scope = _factory.Services.CreateAsyncScope();
        var repo = new PropertyRepository(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        Assert.NotNull(await repo.GetPublishedByIdWithDetailsAsync(available));
        Assert.NotNull(await repo.GetPublishedByIdWithDetailsAsync(reserved));
        Assert.NotNull(await repo.GetPublishedByIdWithDetailsAsync(sold));     // past its window, still documented
        Assert.NotNull(await repo.GetPublishedByIdWithDetailsAsync(rented));
        Assert.Null(await repo.GetPublishedByIdWithDetailsAsync(archived));
        Assert.False(await repo.IsPubliclyVisibleAsync(archived));
        Assert.True(await repo.IsPubliclyVisibleAsync(sold));
    }

    [Fact(DisplayName = "Sold and Rented listings do not use up the owner's or the agency's listing quota")]
    public async Task ClosedDeals_DoNotCountTowardsTheQuota()
    {
        var agency = await NewAgencyAsync();
        await AddAsync("live", agencyId: agency);
        await AddAsync("sold", status: PropertyStatus.Sold, agencyId: agency);
        await AddAsync("rented", status: PropertyStatus.Rented, agencyId: agency);

        await using var scope = _factory.Services.CreateAsyncScope();
        var repo = new PropertyRepository(scope.ServiceProvider.GetRequiredService<AppDbContext>());

        Assert.Equal(1, await repo.CountActiveListingsByOwnerAsync(_ownerId));
        Assert.Equal(1, await repo.CountActiveListingsByAgencyAsync(agency));
    }

    // -- geo-search ------------------------------------------------------------------------

    private async Task<JsonElement> GeoAsync(string query, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/properties/geo-search?{query}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"expected {expected}, got {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static string[] GeoTitles(JsonElement root) => root.GetProperty("items").EnumerateArray()
        .Select(i => i.GetProperty("title").GetString()!).Select(t => t[(t.IndexOf(' ') + 1)..]).OrderBy(t => t, StringComparer.Ordinal).ToArray();

    [Fact(DisplayName = "geo-search without coordinates answers 422 naming the missing fields, not an empty list (P7-D07)")]
    public async Task Geo_WithoutCoordinates_IsRefused()
    {
        var root = await GeoAsync("radiusKm=5", HttpStatusCode.UnprocessableEntity);
        var errors = root.GetProperty("errors").ToString();

        Assert.Contains("Latitude", errors);
        Assert.Contains("Longitude", errors);
    }

    [Fact(DisplayName = "geo-search obeys the same status, listing-type and currency rules as the list")]
    public async Task Geo_FollowsTheListRules()
    {
        decimal Rate(string code)
        {
            using var scope = _factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<AppDbContext>().Currencies
                .AsNoTracking().Single(c => c.Code == code).ExchangeRateToUSD;
        }

        // Seeded around a point far from the other tests' data (the sea off Tartus).
        const decimal lat = 34.8m, lng = 34.9m;
        await AddAsync("avail-usd", currency: "USD", price: 80_000m, lat: lat, lng: lng);
        await AddAsync("avail-eur", currency: "EUR", price: 80_000m * Rate("EUR"), lat: lat + 0.001m, lng: lng);
        await AddAsync("avail-syp-high", currency: "SYP", price: 200_000m * Rate("SYP"), lat: lat, lng: lng + 0.001m);
        await AddAsync("archived", status: PropertyStatus.Archived, price: 80_000m, lat: lat, lng: lng);
        await AddAsync("sold", status: PropertyStatus.Sold, price: 80_000m, lat: lat, lng: lng);
        await AddAsync("reserved", status: PropertyStatus.Reserved, price: 80_000m, lat: lat, lng: lng);
        await AddAsync("both", type: ListingType.ForRentAndSale, price: 80_000m, lat: lat, lng: lng);
        await AddAsync("rent", type: ListingType.ForRent, price: 90m, lat: lat, lng: lng);

        var center = $"latitude={lat}&longitude={lng}&radiusKm=2&pageSize=100";

        Assert.Equal(["avail-eur", "avail-syp-high", "avail-usd", "both", "rent", "reserved"], GeoTitles(await GeoAsync(center)));

        // "For sale" includes the combined ad, like the list does.
        Assert.Equal(["avail-eur", "avail-syp-high", "avail-usd", "both", "reserved"], GeoTitles(await GeoAsync($"{center}&listingType=ForSale")));
        Assert.Equal(["both", "rent"], GeoTitles(await GeoAsync($"{center}&listingType=ForRent")));

        // 100,000 USD ceiling keeps the dollar and euro flats, drops the 200,000 USD lira one.
        Assert.Equal(
            ["avail-eur", "avail-usd", "both", "reserved"],
            GeoTitles(await GeoAsync($"{center}&listingType=ForSale&currencyCode=USD&maxPrice=100000")));
    }

    [Fact(DisplayName = "A LIKE wildcard or NUL typed into the geo-search city is literal or refused, never a 500 (P7-D03)")]
    public async Task Geo_CityWildcardsAndNul()
    {
        await GeoAsync("latitude=34.8&longitude=34.9&radiusKm=2&city=%25", HttpStatusCode.OK);
        await GeoAsync("latitude=34.8&longitude=34.9&radiusKm=2&city=a%00b", HttpStatusCode.UnprocessableEntity);
    }

    [Fact(DisplayName = "The public list refuses NUL and absurd page numbers with 422 instead of 500 (P7-D03)")]
    public async Task List_RejectsNulAndHugePages()
    {
        using var client = _factory.CreateClient();

        foreach (var url in new[]
                 {
                     "/api/properties?searchTerm=a%00b", "/api/properties?city=a%00b", "/api/properties?region=%00",
                     "/api/properties?page=2147483647", "/api/properties?minRooms=5&maxRooms=1", "/api/properties?sortBy=Price"
                 })
        {
            var response = await client.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.UnprocessableEntity, $"{url} -> {(int)response.StatusCode}");
        }
    }
}
