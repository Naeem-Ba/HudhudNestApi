using System.Net;
using System.Text.Json;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Pricing;
using HudhudNestApi.Integration.Tests.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Growth;

/// <summary>Growth engine P6: the rebuild job and the anonymous estimate endpoints, on a fresh governorate.</summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "GrowthEngine")]
public sealed class AreaPriceEstimateTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();
    private readonly string _marker = $"ap{Guid.NewGuid():N}"[..14];
    private readonly List<Guid> _owners = [];
    private int _governorateId;

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        for (var i = 0; i < 3; i++)
            _owners.Add((await _factory.SeedUserAsync($"{_marker}{i}@test.local")).UserAccountId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var governorate = Governorate.Create($"محافظة {_marker}", $"Gov {_marker}");
        db.Governorates.Add(governorate);
        await db.SaveChangesAsync();
        _governorateId = governorate.Id;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "Rebuild + estimate: a range with its sample count; too little data says so; drafts and outliers are left out")]
    public async Task Rebuild_ThenEstimate()
    {
        // Nine live sale listings of 100 m² from three owners at 1,000..1,800 USD/m², one absurd price, one draft.
        for (var i = 0; i < 9; i++)
            await SeedAsync(_owners[i % 3], ListingType.ForSale, 100m, 100_000m + i * 10_000m);
        await SeedAsync(_owners[0], ListingType.ForSale, 100m, 9_000_000m);
        await SeedAsync(_owners[1], ListingType.ForSale, 100m, 50_000m, draft: true);
        // Only two rentals: not enough for a rent estimate.
        await SeedAsync(_owners[0], ListingType.ForRent, 80m, 400m);
        await SeedAsync(_owners[1], ListingType.ForRent, 90m, 450m);

        var written = await Job().RunOnceAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.True(written >= 1);

        var anonymous = _factory.CreateClient();
        var sale = await JsonAsync(anonymous, $"/api/price-estimates?kind=Sale&governorateId={_governorateId}&area=120");
        Assert.True(sale.GetProperty("available").GetBoolean());
        Assert.Equal(9, sale.GetProperty("sampleCount").GetInt32());
        Assert.Equal("Governorate", sale.GetProperty("basis").GetString());
        Assert.Equal("Low", sale.GetProperty("confidence").GetString());
        Assert.Equal("USD", sale.GetProperty("currencyCode").GetString());
        // P25 / median / P75 of 1,000..1,800 per m² are 1,200 / 1,400 / 1,600; times 120 m².
        Assert.Equal(144_000m, sale.GetProperty("low").GetDecimal());
        Assert.Equal(168_000m, sale.GetProperty("mid").GetDecimal());
        Assert.Equal(192_000m, sale.GetProperty("high").GetDecimal());

        var rent = await JsonAsync(anonymous, $"/api/price-estimates?kind=Rent&governorateId={_governorateId}&area=85");
        Assert.False(rent.GetProperty("available").GetBoolean());
        Assert.Equal("not_enough_data", rent.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, rent.GetProperty("mid").ValueKind);

        var tiny = await JsonAsync(anonymous, $"/api/price-estimates?kind=Sale&governorateId={_governorateId}&area=3");
        Assert.Equal("area_out_of_range", tiny.GetProperty("reason").GetString());

        var areas = await JsonAsync(anonymous, $"/api/price-estimates/areas?kind=Sale&governorateId={_governorateId}");
        var row = Assert.Single(areas.EnumerateArray());
        Assert.Equal(1400m, row.GetProperty("medianPerSqm").GetDecimal());
        Assert.Equal(9, row.GetProperty("sampleCount").GetInt32());
        Assert.False(row.TryGetProperty("ownerId", out _));

        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await anonymous.GetAsync($"/api/price-estimates?kind=Sale&governorateId={_governorateId}&area=100&currency=XYZ")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await anonymous.GetAsync("/api/price-estimates?kind=Sale&governorateId=0&area=100")).StatusCode);
    }

    private async Task SeedAsync(Guid owner, ListingType type, decimal area, decimal price, bool draft = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var property = Property.Create($"{_marker} {price}", "وصف كافٍ للإعلان", owner, type, currencyCode: "USD");
        property.GovernorateId = _governorateId;
        property.Area = area;
        if (type == ListingType.ForSale)
            property.PurchasePrice = price;
        else
            property.ColdRent = price;
        if (draft)
            property.Unpublish();
        db.Properties.Add(property);
        await db.SaveChangesAsync();
    }

    private static async Task<JsonElement> JsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {(int)response.StatusCode} {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private AreaPriceStatsHostedService Job() => new(
        _factory.Services.GetRequiredService<IServiceScopeFactory>(),
        _factory.Services.GetRequiredService<ILogger<AreaPriceStatsHostedService>>());
}
