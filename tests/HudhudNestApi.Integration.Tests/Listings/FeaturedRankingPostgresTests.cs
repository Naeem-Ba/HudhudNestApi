using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.Properties.Ranking;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Persistence.Seeds;
using HudhudNestApi.Infrastructure.Repositories;
using HudhudNestApi.Integration.Tests.Auth;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// The default list in three tiers (featured in the caller's interest governorates, other featured,
/// everything else), against a real PostgreSQL: tier order, paging that neither repeats nor skips a
/// listing, featured windows that have lapsed, the on-market rule, the explicit sorts that must stay
/// as they were, and the endpoint's cache key and query-count budget. Everything is seeded under a
/// unique marker so the suite can share a database with others.
/// </summary>
[Collection("AuthPostgres")]
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class FeaturedRankingPostgresTests : IAsyncLifetime
{
    private readonly PostgresAuthTestFactory _factory = new();
    private readonly string _marker = $"rk{Guid.NewGuid():N}";
    private static readonly DateTime Base = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private Guid _ownerId;

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        var owner = await _factory.SeedUserAsync($"{_marker}@test.local");
        _ownerId = owner.UserAccountId;

        await using var scope = _factory.Services.CreateAsyncScope();
        await GovernoratesSeed.SeedAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // -- helpers ---------------------------------------------------------------------------

    /// <param name="featuredDays">null = not featured; positive = featured for that many more days;
    /// negative = still flagged, but the paid window ended that many days ago (the sweep has not run).</param>
    /// <param name="age">Older listings have a larger age: CreatedAt = Base - age.</param>
    private async Task<Guid> AddAsync(
        string name, int? governorateId, int age, double? featuredDays = null,
        PropertyStatus status = PropertyStatus.Available, DateTime? expiresAt = null, decimal? price = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var property = Property.Create($"{_marker} {name}", "وصف كافٍ للإعلان", _ownerId, ListingType.ForSale, currencyCode: "USD");
        property.Status = status;
        property.GovernorateId = governorateId;
        property.ExpiresAt = expiresAt;
        property.PurchasePrice = price;
        if (featuredDays is > 0)
            property.MarkFeatured(TimeSpan.FromDays(featuredDays.Value), DateTime.UtcNow);
        else if (featuredDays is < 0)
            property.MarkFeatured(TimeSpan.FromDays(1), DateTime.UtcNow.AddDays(featuredDays.Value - 1));

        db.Properties.Add(property);
        await db.SaveChangesAsync();

        var createdAt = Base.AddMinutes(-age);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Properties\" SET \"CreatedAt\" = {createdAt} WHERE \"Id\" = {property.Id}");
        return property.Id;
    }

    private async Task<PagedResult<Property>> ListAsync(int page, int pageSize, string? interest, string? sortBy = null, bool descending = true)
    {
        var filter = new PropertyFilterDto
        {
            SearchTerm = _marker,
            Page = page,
            PageSize = pageSize,
            InterestGovernorateIds = interest,
            SortDescending = descending
        };
        if (sortBy is not null) filter.SortBy = sortBy;

        await using var scope = _factory.Services.CreateAsyncScope();
        return await new PropertyRepository(scope.ServiceProvider.GetRequiredService<AppDbContext>())
            .GetPagedAsync(filter, CancellationToken.None);
    }

    private static string[] Names(PagedResult<Property> page)
        => page.Items.Select(p => p.Title[(p.Title.IndexOf(' ') + 1)..]).ToArray();

    // -- tiers -----------------------------------------------------------------------------

    [Fact(DisplayName = "Featured ads of an interest governorate come first, then other ads, then the rest, newest first in each")]
    public async Task ThreeTiers_AreOrdered()
    {
        await AddAsync("plain-newest", 5, age: 1);
        await AddAsync("ad-other-newer", 5, age: 10, featuredDays: 10);
        await AddAsync("ad-area-older", 3, age: 40, featuredDays: 10);
        await AddAsync("plain-mid", 3, age: 20);
        await AddAsync("ad-area-newer", 3, age: 30, featuredDays: 10);
        await AddAsync("plain-oldest", null, age: 90);

        var page = await ListAsync(1, 100, interest: "3");

        Assert.Equal(
            ["ad-area-newer", "ad-area-older", "ad-other-newer", "plain-newest", "plain-mid", "plain-oldest"],
            Names(page));
        Assert.Equal(6, page.TotalCount);
    }

    [Fact(DisplayName = "Several interest governorates count as one set; unknown ids and a missing list change nothing else")]
    public async Task InterestSet_AndNoInterest()
    {
        await AddAsync("ad-gov3", 3, age: 50, featuredDays: 5);
        await AddAsync("ad-gov7", 7, age: 60, featuredDays: 5);
        await AddAsync("ad-gov5-newest", 5, age: 5, featuredDays: 5);
        await AddAsync("plain", 3, age: 1);

        Assert.Equal(["ad-gov3", "ad-gov7", "ad-gov5-newest", "plain"], Names(await ListAsync(1, 100, "7,3")));
        // Without interest, ads are simply newest first among themselves.
        Assert.Equal(["ad-gov5-newest", "ad-gov3", "ad-gov7", "plain"], Names(await ListAsync(1, 100, null)));
        // A governorate id nobody uses behaves like no interest.
        Assert.Equal(["ad-gov5-newest", "ad-gov3", "ad-gov7", "plain"], Names(await ListAsync(1, 100, "14")));
    }

    [Fact(DisplayName = "A featured window that has ended is an ordinary listing even before the sweep clears the flag")]
    public async Task LapsedAd_IsOrdinary()
    {
        await AddAsync("ad-live", 5, age: 100, featuredDays: 3);
        var lapsed = await AddAsync("ad-lapsed", 3, age: 1, featuredDays: -2);
        await AddAsync("plain", 3, age: 2);

        var page = await ListAsync(1, 100, interest: "3");

        Assert.Equal(["ad-live", "ad-lapsed", "plain"], Names(page));    // the lapsed ad sits by its date, not on top
        Assert.True(page.Items.Single(p => p.Id == lapsed).IsFeatured);   // the raw flag is still set: only the clock decides
    }

    [Fact(DisplayName = "Only listings on the market are ranked: sold, expired and unpublished ads never appear")]
    public async Task OnlyOnMarketListings()
    {
        await AddAsync("ad-sold", 3, age: 1, featuredDays: 9, status: PropertyStatus.Sold);
        await AddAsync("ad-expired-date", 3, age: 2, featuredDays: 9, expiresAt: DateTime.UtcNow.AddDays(-1));
        await AddAsync("ad-reserved", 3, age: 3, featuredDays: 9, status: PropertyStatus.Reserved);
        await AddAsync("plain-sold", 3, age: 4, status: PropertyStatus.Sold);
        await AddAsync("plain", 3, age: 5);

        var page = await ListAsync(1, 100, interest: "3");

        Assert.Equal(["ad-reserved", "plain"], Names(page));
        Assert.Equal(2, page.TotalCount);
    }

    // -- paging ----------------------------------------------------------------------------

    [Fact(DisplayName = "Paging through the list repeats nothing and skips nothing, and matches the page arithmetic")]
    public async Task Paging_IsDuplicateFree_AndMatchesTheModel()
    {
        // 14 ads (governorates 1..7, two each, mixed ages) and 26 ordinary listings.
        var featured = new List<(Guid Id, int Gov, int Age)>();
        for (var i = 0; i < 14; i++)
        {
            var gov = i % 7 + 1;
            var age = 100 + (i * 37) % 91;                                 // distinct ages
            featured.Add((await AddAsync($"ad{i:00}", gov, age, featuredDays: 5), gov, age));
        }
        var ordinary = new List<(Guid Id, int Age)>();
        for (var i = 0; i < 26; i++)
        {
            var age = 1 + i * 3;
            ordinary.Add((await AddAsync($"pl{i:00}", i % 14 + 1, age), age));
        }

        const string interest = "2,5";
        var interestSet = new[] { 2, 5 };
        var expectedFeatured = featured
            .OrderBy(f => interestSet.Contains(f.Gov) ? 0 : 1).ThenBy(f => f.Age).ThenBy(f => f.Id).Select(f => f.Id).ToList();
        var expectedOrdinary = ordinary.OrderBy(o => o.Age).ThenBy(o => o.Id).Select(o => o.Id).ToList();

        foreach (var pageSize in new[] { 6, 8, 20 })
        {
            var seen = new List<Guid>();
            var lastPage = (40 + pageSize - 1) / pageSize;
            for (var page = 1; page <= lastPage; page++)
            {
                var result = await ListAsync(page, pageSize, interest);
                Assert.Equal(40, result.TotalCount);

                var slice = FeaturedPageLayout.Plan(page, pageSize, 14, 26, configuredMax: 5);
                var expected = expectedFeatured.Skip(slice.FeaturedSkip).Take(slice.FeaturedTake)
                    .Concat(expectedOrdinary.Skip(slice.OrdinarySkip).Take(slice.OrdinaryTake)).ToList();
                Assert.Equal(expected, result.Items.Select(p => p.Id).ToList());

                // Never more ads than the cap while ordinary listings remain (the last pages may be topped
                // up with ads once they run out), and the ads lead the page.
                var ads = result.Items.TakeWhile(p => p.IsFeatured).Count();
                Assert.Equal(slice.FeaturedTake, ads);
                Assert.All(result.Items.Skip(ads), p => Assert.False(p.IsFeatured));
                var cap = FeaturedPageLayout.CapFor(pageSize, 5);
                if (26 - slice.OrdinarySkip > pageSize - cap)   // ordinary listings still fill the rest of the page
                    Assert.True(ads <= cap, $"page {page} (size {pageSize}) shows {ads} ads, cap {cap}");

                seen.AddRange(result.Items.Select(p => p.Id));
            }

            Assert.Equal(40, seen.Count);
            Assert.Equal(40, seen.Distinct().Count());
        }
    }

    [Fact(DisplayName = "The total does not depend on how the list is ordered")]
    public async Task TotalCount_IsTheSameForEveryOrder()
    {
        for (var i = 0; i < 5; i++) await AddAsync($"ad{i}", i + 1, age: i, featuredDays: 4, price: 1000 + i);
        for (var i = 0; i < 12; i++) await AddAsync($"pl{i}", 3, age: 10 + i, price: 500 + i);

        var tiered = await ListAsync(1, 6, "3");
        var byPrice = await ListAsync(1, 6, "3", sortBy: "PurchasePrice", descending: false);
        var oldestFirst = await ListAsync(1, 6, "3", sortBy: "CreatedAt", descending: false);

        Assert.Equal(17, tiered.TotalCount);
        Assert.Equal(17, byPrice.TotalCount);
        Assert.Equal(17, oldestFirst.TotalCount);
    }

    [Fact(DisplayName = "An explicit order (price) is not touched: ads get no priority there")]
    public async Task ExplicitSort_KeepsItsOwnOrder()
    {
        await AddAsync("cheap", 3, age: 5, price: 100);
        await AddAsync("ad-dear", 3, age: 6, featuredDays: 5, price: 900);
        await AddAsync("mid", 3, age: 7, price: 500);

        var page = await ListAsync(1, 100, "3", sortBy: "PurchasePrice", descending: false);

        Assert.Equal(["cheap", "mid", "ad-dear"], Names(page));
    }

    [Fact(DisplayName = "A page beyond the last one is empty but still reports the total")]
    public async Task PageBeyondTheEnd_IsEmpty()
    {
        await AddAsync("only", 3, age: 1);

        var page = await ListAsync(5, 20, "3");

        Assert.Empty(page.Items);
        Assert.Equal(1, page.TotalCount);
    }

    // -- the endpoint -----------------------------------------------------------------------

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    [Fact(DisplayName = "GET /api/Properties flags featured ads of the interest area, keys the cache by it and stays within three commands")]
    public async Task Endpoint_FlagsTheArea_VariesTheCache_AndKeepsTheQueryBudget()
    {
        Environment.SetEnvironmentVariable("Performance__ExposeDatabaseDiagnostics", "true");
        try
        {
            await AddAsync("ad-three", 3, age: 50, featuredDays: 9);
            await AddAsync("ad-five", 5, age: 60, featuredDays: 9);
            await AddAsync("plain", 3, age: 1);
            var client = _factory.WithWebHostBuilder(_ => { }).CreateClient();
            string Url(string interest) => $"/api/Properties?searchTerm={_marker}&pageSize=20&interestGovernorateIds={interest}";

            var forThree = await client.GetAsync(Url("3"));
            var three = await JsonAsync(forThree);
            Assert.True(forThree.Headers.TryGetValues("X-Database-Command-Count", out var count));
            Assert.InRange(int.Parse(count!.Single()), 1, 3);

            var forFive = await JsonAsync(await client.GetAsync(Url("5")));

            string[] Titles(JsonElement e) => e.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()![(_marker.Length + 1)..]).ToArray();
            bool[] Flags(JsonElement e) => e.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("matchesInterestArea").GetBoolean()).ToArray();

            // Different interest, different answer: a cached reply for "3" must not be served for "5".
            Assert.Equal(["ad-three", "ad-five", "plain"], Titles(three));
            Assert.Equal([true, false, false], Flags(three));
            Assert.Equal(["ad-five", "ad-three", "plain"], Titles(forFive));
            Assert.Equal([true, false, false], Flags(forFive));
        }
        finally
        {
            Environment.SetEnvironmentVariable("Performance__ExposeDatabaseDiagnostics", null);
        }
    }

    [Theory(DisplayName = "A malformed interest list is a 422 naming the parameter, not a 500")]
    [InlineData("1,2,3,4")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("3,,5")]
    public async Task Endpoint_RejectsMalformedInterest(string interest)
    {
        var client = _factory.WithWebHostBuilder(_ => { }).CreateClient();

        var response = await client.GetAsync($"/api/Properties?interestGovernorateIds={Uri.EscapeDataString(interest)}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("InterestGovernorateIds", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }
}
