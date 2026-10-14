using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Analytics;

/// <summary>
/// QA Phase 6: the public GET /api/analytics/market answered 500 on every call, because the grouped
/// projection used g.Where(...).Average(...) which EF Core cannot translate. Runs the real query on Postgres.
/// </summary>
public sealed class MarketInsightsTests : IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "Market insights answer 200 with per-city averages for rent and sale")]
    public async Task Market_ReturnsAveragesPerCity()
    {
        var owner = await _factory.SeedUserAsync("market-owner");
        var ids = new[]
        {
            await _factory.SeedPropertyAsync(owner.Id),
            await _factory.SeedPropertyAsync(owner.Id),
            await _factory.SeedPropertyAsync(owner.Id),
        };

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            async Task Set(Guid id, ListingType type, decimal? rent, decimal? price) =>
                await db.Properties.Where(p => p.Id == id).ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.IsPublished, true)
                    .SetProperty(p => p.City, "Marketville")
                    .SetProperty(p => p.CountryCode, "SY")
                    .SetProperty(p => p.ListingType, type)
                    .SetProperty(p => p.ColdRent, rent)
                    .SetProperty(p => p.PurchasePrice, price));

            await Set(ids[0], ListingType.ForRent, 100m, null);
            await Set(ids[1], ListingType.ForRent, 300m, null);
            await Set(ids[2], ListingType.ForSale, null, 50_000m);
        });

        var response = await _factory.CreateClient().GetAsync("/api/analytics/market?countryCode=SY");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        using var doc = JsonDocument.Parse(body);
        var city = doc.RootElement.GetProperty("cities").EnumerateArray()
            .Single(c => c.GetProperty("city").GetString() == "Marketville");
        Assert.Equal(3, city.GetProperty("totalActiveListings").GetInt32());
        Assert.Equal(200m, city.GetProperty("avgPriceForRent").GetDecimal());
        Assert.Equal(50_000m, city.GetProperty("avgPriceForSale").GetDecimal());
    }
}
