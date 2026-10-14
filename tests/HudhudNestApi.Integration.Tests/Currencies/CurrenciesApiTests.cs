using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Persistence.Seeds;
using HudhudNestApi.Infrastructure.Repositories;
using HudhudNestApi.Integration.Tests.AppUpdates;

namespace HudhudNestApi.Integration.Tests.Currencies;

/// <summary>
/// GET /api/lookups/currencies (public) and PUT /api/admin/currencies/{code} (administrators): the rates
/// the search and the clients use to compare prices advertised in different currencies (QA Phase 7).
/// </summary>
[Trait("Feature", "Currencies")]
public sealed class CurrenciesApiTests : IClassFixture<AppUpdateApiTestFactory>, IAsyncLifetime
{
    private readonly AppUpdateApiTestFactory _factory;

    public CurrenciesApiTests(AppUpdateApiTestFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        using var scope = _factory.Services.CreateScope();
        await CurrencySeed.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static object Body(decimal rate, bool? active = null) => new
    {
        nameEn = "Euro",
        nameAr = "اليورو",
        symbol = "€",
        decimalPlaces = 2,
        exchangeRateToUsd = rate,
        isActive = active
    };

    [Fact(DisplayName = "Anyone can read the active currencies; codes are trimmed and the base currency comes first")]
    public async Task Public_List()
    {
        var response = await _factory.CreateClient().GetAsync("/api/lookups/currencies");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.Public);

        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        Assert.Equal("USD", items[0].GetProperty("code").GetString());
        Assert.True(items[0].GetProperty("isBase").GetBoolean());
        Assert.All(items, i => Assert.Equal(i.GetProperty("code").GetString()!.Trim(), i.GetProperty("code").GetString()));
        // Only the search currencies are offered: the base first, then by code.
        Assert.Equal(["USD", "EUR", "SYP"], items.Select(i => i.GetProperty("code").GetString()!).ToArray());
    }

    [Fact(DisplayName = "An administrator sets a rate, it shows in the public list, and the change is audited")]
    public async Task Admin_UpdatesRate()
    {
        var admin = await _factory.SeedUserAsync("admin-currency", RoleNames.Admin);
        var client = _factory.AuthedClient(admin.AccessToken);

        var response = await client.PutAsJsonAsync("/api/admin/currencies/eur", Body(0.9m));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var list = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/lookups/currencies");
        var eurRow = list.EnumerateArray().Single(i => i.GetProperty("code").GetString() == "EUR");
        Assert.Equal(0.9m, eurRow.GetProperty("exchangeRateToUsd").GetDecimal());

        using var scope = _factory.Services.CreateScope();
        var actions = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs
            .AsNoTracking().Select(a => a.Action).ToListAsync();
        Assert.Contains(AuditActions.CurrencyRateUpdatedByAdmin, actions);
    }

    [Fact]
    public async Task Admin_CanAddACurrency()
    {
        var admin = await _factory.SeedUserAsync("admin-currency-new", RoleNames.Admin);
        var client = _factory.AuthedClient(admin.AccessToken);

        var response = await client.PutAsJsonAsync("/api/admin/currencies/GBP", new
        {
            nameEn = "Pound Sterling",
            nameAr = "الجنيه الإسترليني",
            symbol = "£",
            decimalPlaces = 2,
            exchangeRateToUsd = 0.78m
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // A currency outside the search set is stored but neither offered nor used to convert prices.
        var list = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/lookups/currencies");
        Assert.DoesNotContain(list.EnumerateArray(), i => i.GetProperty("code").GetString() == "GBP");

        using var scope = _factory.Services.CreateScope();
        var rates = await PropertyRepository.LoadCurrencyRatesAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        Assert.False(rates.TryGetRate("GBP", out _));
        Assert.True(rates.TryGetRate("SYP", out _));
    }

    [Fact(DisplayName = "Searching by a currency outside the search set is refused with 422")]
    public async Task Search_RefusesOtherCurrencies()
    {
        var response = await _factory.CreateClient().GetAsync("/api/properties?currencyCode=TRY&maxPrice=1000");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Admin_RefusesNonPositiveRate(decimal rate)
    {
        var admin = await _factory.SeedUserAsync("admin-currency-bad", RoleNames.Admin);
        var response = await _factory.AuthedClient(admin.AccessToken).PutAsJsonAsync("/api/admin/currencies/TRY", Body(rate));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact(DisplayName = "The base currency's rate cannot be changed")]
    public async Task Admin_CannotChangeTheBaseRate()
    {
        var admin = await _factory.SeedUserAsync("admin-currency-base", RoleNames.Admin);
        var response = await _factory.AuthedClient(admin.AccessToken).PutAsJsonAsync("/api/admin/currencies/USD", Body(2m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "Anonymous callers and ordinary users cannot change rates")]
    public async Task NonAdmins_AreRefused()
    {
        var anonymous = await _factory.CreateClient().PutAsJsonAsync("/api/admin/currencies/TRY", Body(50m));
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        var user = await _factory.SeedUserAsync("plain-currency");
        var forbidden = await _factory.AuthedClient(user.AccessToken).PutAsJsonAsync("/api/admin/currencies/TRY", Body(50m));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }
}
