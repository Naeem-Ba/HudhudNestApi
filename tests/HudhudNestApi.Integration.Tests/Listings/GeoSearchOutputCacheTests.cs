using System.Net;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Configuration;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Auth;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// GET /api/properties/geo-search is output-cached (audit B3: it was the only public property
/// read without a cache, and its exact COUNT over the whole radius dominated PostgreSQL CPU).
/// Exercised over real HTTP against real PostgreSQL/PostGIS because the repository runs raw
/// spatial SQL. A cache hit is observable as an Age header on the replayed response.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class GeoSearchOutputCacheTests : IAsyncLifetime
{
    private const string Query = "latitude=33.5138&longitude=36.2765&radiusKm=5&page=1&pageSize=5";

    private readonly PostgresAuthTestFactory _factory = new();

    public async Task InitializeAsync() => await _factory.PrepareDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "An identical geo-search is served from the output cache and carries the public Cache-Control")]
    public async Task IdenticalRequest_IsServedFromCache()
    {
        using var client = _factory.CreateClient();

        var first = await client.GetAsync($"/api/properties/geo-search?{Query}");
        var second = await client.GetAsync($"/api/properties/geo-search?{Query}");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False(first.Headers.Contains("Age"), "the first request must be a cache miss");
        Assert.True(second.Headers.Contains("Age"), "the identical second request must be a cache hit");
        Assert.True(second.Headers.CacheControl!.Public);
        Assert.Equal(OutputCacheRegistration.PublicPropertyListMaxAge, second.Headers.CacheControl.MaxAge);
    }

    [Fact(DisplayName = "geo-search requests that differ in any parameter do not share a cache entry")]
    public async Task DifferentParameters_DoNotShareAnEntry()
    {
        using var client = _factory.CreateClient();
        await client.GetAsync($"/api/properties/geo-search?{Query}");

        var otherCoordinates = await client.GetAsync(
            "/api/properties/geo-search?latitude=33.5139&longitude=36.2765&radiusKm=5&page=1&pageSize=5");
        var otherRadius = await client.GetAsync(
            "/api/properties/geo-search?latitude=33.5138&longitude=36.2765&radiusKm=6&page=1&pageSize=5");
        var otherFilter = await client.GetAsync($"/api/properties/geo-search?{Query}&listingType=ForSale");

        Assert.False(otherCoordinates.Headers.Contains("Age"));
        Assert.False(otherRadius.Headers.Contains("Age"));
        Assert.False(otherFilter.Headers.Contains("Age"));
    }

    [Fact(DisplayName = "A property write evicts cached geo-search responses")]
    public async Task PropertyWrite_EvictsTheCachedResponse()
    {
        var owner = await _factory.SeedUserAsync($"geo-cache-{Guid.NewGuid():N}@test.local");
        using var client = _factory.CreateClient();

        await client.GetAsync($"/api/properties/geo-search?{Query}");
        var cached = await client.GetAsync($"/api/properties/geo-search?{Query}");
        Assert.True(cached.Headers.Contains("Age"));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Properties.Add(Property.Create(
                "شقة للإيجار", "وصف كافٍ للإعلان", owner.UserAccountId, ListingType.ForRent));
            await db.SaveChangesAsync();
        }

        var afterWrite = await client.GetAsync($"/api/properties/geo-search?{Query}");

        Assert.Equal(HttpStatusCode.OK, afterWrite.StatusCode);
        Assert.False(afterWrite.Headers.Contains("Age"), "a property write must evict the cached response");
    }

    [Fact(DisplayName = "A rejected geo-search (400) is neither cached nor marked cacheable")]
    public async Task RejectedRequest_IsNotCached()
    {
        using var client = _factory.CreateClient();
        const string invalid = "latitude=999&longitude=36.2765&radiusKm=5";

        var first = await client.GetAsync($"/api/properties/geo-search?{invalid}");
        var second = await client.GetAsync($"/api/properties/geo-search?{invalid}");

        Assert.NotEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.False(second.Headers.Contains("Age"));
        Assert.True(first.Headers.CacheControl?.Public != true);
    }
}
