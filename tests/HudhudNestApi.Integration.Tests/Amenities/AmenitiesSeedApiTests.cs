using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Persistence.Seeds;
using HudhudNestApi.Integration.Tests.AppUpdates;

namespace HudhudNestApi.Integration.Tests.Amenities;

/// <summary>
/// A fresh database used to answer GET /api/lookups/amenities with an empty list because no code
/// path ever created the rows, so every listing form said "no amenities available". The seed fills
/// the reference list, runs again without duplicating anything, and leaves rows an admin made alone.
/// </summary>
[Trait("Feature", "Amenities")]
public sealed class AmenitiesSeedApiTests : IClassFixture<AppUpdateApiTestFactory>, IAsyncLifetime
{
    private readonly AppUpdateApiTestFactory _factory;

    public AmenitiesSeedApiTests(AppUpdateApiTestFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        await WithContextAsync(async ctx =>
        {
            await ctx.Database.ExecuteSqlRawAsync("""DELETE FROM "PropertyAmenities"; DELETE FROM "Amenities";""");
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task WithContextAsync(Func<AppDbContext, Task> action)
    {
        using var scope = _factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    [Fact(DisplayName = "Seeding twice gives exactly the catalog, once each")]
    public async Task Seed_IsIdempotent()
    {
        await WithContextAsync(AmenitiesSeed.SeedAsync);
        await WithContextAsync(AmenitiesSeed.SeedAsync);

        await WithContextAsync(async ctx =>
        {
            Assert.Equal(AmenitiesSeed.Catalog.Count, await ctx.Amenities.CountAsync());
            Assert.Equal(AmenitiesSeed.Catalog.Count, await ctx.Amenities.Select(a => a.Name).Distinct().CountAsync());
        });
    }

    [Fact(DisplayName = "A row an admin already made with the same Arabic name is kept, not duplicated or renamed")]
    public async Task Seed_LeavesExistingRowsAlone()
    {
        var first = AmenitiesSeed.Catalog[0];
        await WithContextAsync(async ctx =>
        {
            ctx.Amenities.Add(new HudhudNestApi.Domain.Listings.Entities.Amenity { Name = first.NameAr, Category = "Custom" });
            await ctx.SaveChangesAsync();
        });

        await WithContextAsync(AmenitiesSeed.SeedAsync);

        await WithContextAsync(async ctx =>
        {
            Assert.Equal(AmenitiesSeed.Catalog.Count, await ctx.Amenities.CountAsync());
            Assert.Equal("Custom", (await ctx.Amenities.SingleAsync(a => a.Name == first.NameAr)).Category);
        });
    }

    [Fact(DisplayName = "The public lookup returns the seeded list with name and icon key")]
    public async Task Lookup_ReturnsSeededAmenities()
    {
        await WithContextAsync(AmenitiesSeed.SeedAsync);

        var response = await _factory.CreateClient().GetAsync("/api/lookups/amenities");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var list = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        Assert.True(list.Count >= AmenitiesSeed.Catalog.Count);
        Assert.Contains(list, a => a.GetProperty("iconName").GetString() == "wifi"
                                   && !string.IsNullOrWhiteSpace(a.GetProperty("name").GetString()));
    }
}
