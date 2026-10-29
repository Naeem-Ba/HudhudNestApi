using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Listings.Views;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Listings.Enums;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Domain.ShortStay.Enums;
using HudhudNestApi.Infrastructure.Listings;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Messaging;
using HudhudNestApi.Integration.Tests.TestInfrastructure;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// The listing view counter over real HTTP and PostgreSQL: what counts as a view, who is never counted,
/// exactly-once under concurrency, the owner/agency statistics permissions, retention.
/// </summary>
[Trait("Feature", "Listings")]
public sealed class ListingViewCounterTests : IClassFixture<MessagingApiTestFactory>, IAsyncLifetime
{
    private const string Browser = "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/120 Mobile Safari/537.36";
    private readonly MessagingApiTestFactory _factory;

    public ListingViewCounterTests(MessagingApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static HttpRequestMessage ViewRequest(string url, string? visitor, string? userAgent = Browser, string? bearer = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (visitor is not null) request.Headers.Add("X-Visitor-Id", visitor);
        if (userAgent is not null) request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        if (bearer is not null) request.Headers.Authorization = new("Bearer", bearer);
        return request;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    private async Task<long> StoredCountAsync(ListingKind kind, Guid id)
        => await _factory.InScopeAsync(async s =>
            (await s.GetRequiredService<AppDbContext>().ListingViewCounters.AsNoTracking()
                .Where(c => c.Kind == kind && c.ListingId == id).Select(c => (long?)c.ViewCount).FirstOrDefaultAsync()) ?? 0);

    private async Task<Guid> SeedShortStayAsync(Guid ownerId, bool publish)
        => await _factory.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            var type = await db.AccommodationTypes.FirstOrDefaultAsync();
            if (type is null)
            {
                type = AccommodationType.Create($"test-{Guid.NewGuid():N}"[..20], "شقة", "Apartment", "Residential", null, 1);
                db.AccommodationTypes.Add(type);
                await db.SaveChangesAsync();
            }

            var governorate = Governorate.Create($"حمص-{Guid.NewGuid():N}"[..12], $"Homs-{Guid.NewGuid():N}"[..12], "SY");
            db.Governorates.Add(governorate);
            await db.SaveChangesAsync();

            var listing = ShortStayListing.Create(
                ownerId, type.Id, "شاليه للعدّاد", "وصف كافٍ للإعلان", 4, 2, 1,
                new TimeOnly(14, 0), new TimeOnly(11, 0), null, null);
            listing.UpdateLocation(34.7m, 36.7m, governorate.Id, null, null, governorate.NameAr, LocationVisibility.Exact);
            var room = new RoomType { ShortStayListing = listing, Name = "Standard", BasePricePerNight = 50m };
            db.ShortStayListings.Add(listing);
            db.Add(room);
            db.Add(new AccommodationUnit { RoomType = room, Label = "A1" });
            await db.SaveChangesAsync();
            if (publish)
            {
                listing.Publish();
                await db.SaveChangesAsync();
            }

            return listing.Id;
        });

    // ── What counts ──────────────────────────────────────────────────────

    [Fact(DisplayName = "views: the first open counts, a repeat by the same visitor does not, another visitor does; the total is the server's")]
    public async Task FirstViewCounts_RepeatDoesNot_AnotherVisitorDoes()
    {
        var owner = await _factory.SeedUserAsync("v-owner");
        var id = await _factory.SeedPropertyAsync(owner.Id);
        var client = _factory.CreateClient();
        var url = $"/api/properties/{id}/views";

        var first = await Json(await client.SendAsync(ViewRequest(url, "aaaaaaaaaaaaaaaa-visitor-1")));
        Assert.True(first.GetProperty("counted").GetBoolean());
        Assert.Equal(1, first.GetProperty("viewCount").GetInt64());

        var again = await Json(await client.SendAsync(ViewRequest(url, "aaaaaaaaaaaaaaaa-visitor-1")));
        Assert.False(again.GetProperty("counted").GetBoolean());
        Assert.Equal("cooldown", again.GetProperty("reason").GetString());
        Assert.Equal(1, again.GetProperty("viewCount").GetInt64());

        var other = await Json(await client.SendAsync(ViewRequest(url, "bbbbbbbbbbbbbbbb-visitor-2")));
        Assert.True(other.GetProperty("counted").GetBoolean());
        Assert.Equal(2, other.GetProperty("viewCount").GetInt64());

        Assert.Equal(2, await StoredCountAsync(ListingKind.Property, id));

        // Fetching the listing does not count; it only reports the total.
        var detail = await Json(await client.GetAsync($"/api/Properties/{id}"));
        Assert.Equal(2, detail.GetProperty("viewCount").GetInt64());
        await client.GetAsync($"/api/Properties/{id}");
        Assert.Equal(2, await StoredCountAsync(ListingKind.Property, id));
    }

    [Fact(DisplayName = "views: the public list carries each total without a fourth SQL command (query-count budget is 3)")]
    public async Task PublicList_ReportsViewCounts_WithinTheListQueryBudget()
    {
        // The perf gate fails a list response that runs more than 3 commands (performance-budgets.json).
        Environment.SetEnvironmentVariable("Performance__ExposeDatabaseDiagnostics", "true");
        try
        {
            var owner = await _factory.SeedUserAsync("v-list-owner");
            var id = await _factory.SeedPropertyAsync(owner.Id);
            var client = _factory.WithWebHostBuilder(_ => { }).CreateClient();
            Assert.True((await client.SendAsync(ViewRequest($"/api/Properties/{id}/views", "aaaaaaaaaaaaaaaa-visitor-1"))).IsSuccessStatusCode);

            var response = await client.GetAsync("/api/Properties?page=1&pageSize=20");
            var list = await Json(response);
            Assert.Equal(1, list.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id)
                .GetProperty("viewCount").GetInt64());

            Assert.True(response.Headers.TryGetValues("X-Database-Command-Count", out var count));
            Assert.InRange(int.Parse(count!.Single()), 0, 3);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Performance__ExposeDatabaseDiagnostics", null);
        }
    }

    [Fact(DisplayName = "views: a viewer is counted again once the cooldown window has passed (new bucket)")]
    public async Task SameViewer_IsCountedAgain_InTheNextWindow()
    {
        var owner = await _factory.SeedUserAsync("v-owner");
        var id = await _factory.SeedPropertyAsync(owner.Id);

        var results = await _factory.InScopeAsync(async s =>
        {
            var repo = s.GetRequiredService<IListingViewRepository>();
            var hash = new string('a', 64);
            var now = DateTime.UtcNow;
            return new[]
            {
                await repo.TryRecordAsync(ListingKind.Property, id, hash, 100, now),
                await repo.TryRecordAsync(ListingKind.Property, id, hash, 100, now),
                await repo.TryRecordAsync(ListingKind.Property, id, hash, 101, now),
            };
        });

        Assert.Equal(new[] { (true, 1L), (false, 1L), (true, 2L) }, results);
    }

    [Fact(DisplayName = "views: a listing that does not exist or is not public is a 404 and nothing is stored")]
    public async Task HiddenAndUnknownListings_AreNotFound()
    {
        var owner = await _factory.SeedUserAsync("v-owner");
        var client = _factory.CreateClient();

        var unknown = await client.SendAsync(ViewRequest($"/api/properties/{Guid.NewGuid()}/views", "aaaaaaaaaaaaaaaa-visitor-1"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var archived = await _factory.SeedPropertyAsync(owner.Id, "مؤرشف");
        await _factory.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            var p = await db.Properties.SingleAsync(x => x.Id == archived);
            p.ChangeStatus(HudhudNestApi.Domain.Enums.PropertyStatus.Archived);
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(ViewRequest($"/api/properties/{archived}/views", "aaaaaaaaaaaaaaaa-visitor-1"))).StatusCode);
        Assert.Equal(0, await StoredCountAsync(ListingKind.Property, archived));

        var draft = await SeedShortStayAsync(owner.Id, publish: false);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(ViewRequest($"/api/short-stay/listings/{draft}/views", "aaaaaaaaaaaaaaaa-visitor-1"))).StatusCode);
        Assert.Equal(0, await StoredCountAsync(ListingKind.ShortStay, draft));
    }

    [Fact(DisplayName = "views: short-stay listings have the same counter and show it in detail, search and 'mine'")]
    public async Task ShortStay_IsCounted_AndShown()
    {
        var host = await _factory.SeedUserAsync("v-host");
        var id = await SeedShortStayAsync(host.Id, publish: true);
        var client = _factory.CreateClient();

        var counted = await Json(await client.SendAsync(ViewRequest($"/api/short-stay/listings/{id}/views", "aaaaaaaaaaaaaaaa-visitor-1")));
        Assert.True(counted.GetProperty("counted").GetBoolean());
        Assert.Equal(1, counted.GetProperty("viewCount").GetInt64());

        var detail = await Json(await client.GetAsync($"/api/short-stay/listings/{id}"));
        Assert.Equal(1, detail.GetProperty("viewCount").GetInt64());

        var mine = await Json(await _factory.AuthedClient(host.AccessToken).GetAsync("/api/short-stay/listings/mine"));
        Assert.Equal(1, mine.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id).GetProperty("viewCount").GetInt64());
    }

    // ── What never counts ────────────────────────────────────────────────

    [Fact(DisplayName = "views: crawlers, requests without a user agent, owners, admins and agency staff are not counted")]
    public async Task BotsOwnersAdminsAndAgencyStaff_AreNotCounted()
    {
        var owner = await _factory.SeedUserAsync("v-owner");
        var admin = await _factory.SeedUserAsync("v-admin", "Admin");
        var staff = await _factory.SeedUserAsync("v-staff");
        var outsider = await _factory.SeedUserAsync("v-outsider");
        var id = await _factory.SeedPropertyAsync(owner.Id);
        await _factory.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            var agency = Agency.Create("Office", $"office-{Guid.NewGuid():N}", owner.Id, "SY", now);
            db.Agencies.Add(agency);
            await db.SaveChangesAsync();
            (await db.UserAccounts.SingleAsync(a => a.Id == staff.Id)).JoinAgency(agency.Id, now);
            (await db.Properties.SingleAsync(p => p.Id == id)).SetAgency(agency.Id);
            await db.SaveChangesAsync();
        });
        var client = _factory.CreateClient();
        var url = $"/api/properties/{id}/views";

        async Task<string?> Reason(HttpRequestMessage request)
        {
            var body = await Json(await client.SendAsync(request));
            Assert.False(body.GetProperty("counted").GetBoolean());
            return body.GetProperty("reason").GetString();
        }

        Assert.Equal("automated", await Reason(ViewRequest(url, "aaaaaaaaaaaaaaaa-visitor-1", "Googlebot/2.1 (+http://www.google.com/bot.html)")));
        Assert.Equal("automated", await Reason(ViewRequest(url, "aaaaaaaaaaaaaaaa-visitor-1", "python-requests/2.31")));
        Assert.Equal("automated", await Reason(ViewRequest(url, "aaaaaaaaaaaaaaaa-visitor-1", userAgent: null)));
        Assert.Equal("own-listing", await Reason(ViewRequest(url, null, bearer: owner.AccessToken)));
        Assert.Equal("own-listing", await Reason(ViewRequest(url, null, bearer: admin.AccessToken)));
        Assert.Equal("own-listing", await Reason(ViewRequest(url, null, bearer: staff.AccessToken)));
        Assert.Equal(0, await StoredCountAsync(ListingKind.Property, id));

        // A signed-in stranger is a normal viewer, and is deduped by account, not by token.
        Assert.True((await Json(await client.SendAsync(ViewRequest(url, null, bearer: outsider.AccessToken)))).GetProperty("counted").GetBoolean());
        Assert.False((await Json(await client.SendAsync(ViewRequest(url, "zzzzzzzzzzzzzzzz-other", bearer: outsider.AccessToken)))).GetProperty("counted").GetBoolean());
        Assert.Equal(1, await StoredCountAsync(ListingKind.Property, id));
    }

    [Fact(DisplayName = "views: traffic carrying the configured test secret is never counted; without a configured secret the header does nothing")]
    public async Task TestTraffic_IsNotCounted_OnlyWithTheConfiguredSecret()
    {
        var owner = await _factory.SeedUserAsync("v-owner");
        var id = await _factory.SeedPropertyAsync(owner.Id);

        var withSecret = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["ListingViews:TestTrafficSecret"] = "e2e-secret-value" })));
        var request = ViewRequest($"/api/properties/{id}/views", "aaaaaaaaaaaaaaaa-visitor-1");
        request.Headers.Add("X-HN-Test-Traffic", "e2e-secret-value");
        var body = await Json(await withSecret.CreateClient().SendAsync(request));
        Assert.Equal("test-traffic", body.GetProperty("reason").GetString());

        var wrong = ViewRequest($"/api/properties/{id}/views", "aaaaaaaaaaaaaaaa-visitor-1");
        wrong.Headers.Add("X-HN-Test-Traffic", "guess");
        Assert.True((await Json(await withSecret.CreateClient().SendAsync(wrong))).GetProperty("counted").GetBoolean());

        // Secret not configured (default factory): the header is ignored, the view counts.
        var ignored = ViewRequest($"/api/properties/{id}/views", "bbbbbbbbbbbbbbbb-visitor-2");
        ignored.Headers.Add("X-HN-Test-Traffic", "e2e-secret-value");
        Assert.True((await Json(await _factory.CreateClient().SendAsync(ignored))).GetProperty("counted").GetBoolean());
    }

    // ── Concurrency ──────────────────────────────────────────────────────

    [Fact(DisplayName = "views: 60 concurrent requests from the same viewer add exactly one; 60 distinct viewers add exactly 60")]
    public async Task Concurrency_IsExactlyOnce_AndLosesNoIncrements()
    {
        var owner = await _factory.SeedUserAsync("v-owner");
        var same = await _factory.SeedPropertyAsync(owner.Id, "same");
        var distinct = await _factory.SeedPropertyAsync(owner.Id, "distinct");

        async Task<bool> Record(Guid listing, string hash, long bucket) => await _factory.InScopeAsync(async s =>
            (await s.GetRequiredService<IListingViewRepository>().TryRecordAsync(ListingKind.Property, listing, hash, bucket, DateTime.UtcNow)).Counted);

        var sameResults = await Task.WhenAll(Enumerable.Range(0, 60).Select(_ => Record(same, new string('b', 64), 7)));
        Assert.Equal(1, sameResults.Count(x => x));
        Assert.Equal(1, await StoredCountAsync(ListingKind.Property, same));

        var distinctResults = await Task.WhenAll(Enumerable.Range(0, 60).Select(i => Record(distinct, i.ToString("x").PadLeft(64, '0'), 7)));
        Assert.All(distinctResults, Assert.True);
        Assert.Equal(60, await StoredCountAsync(ListingKind.Property, distinct));
    }

    // ── Statistics permissions ───────────────────────────────────────────

    [Fact(DisplayName = "stats: an owner sees only their own listings; the agency scope and a stranger's trend are refused")]
    public async Task Stats_AreScopedToTheCaller()
    {
        var owner = await _factory.SeedUserAsync("v-owner");
        var other = await _factory.SeedUserAsync("v-other");
        var agencyOwner = await _factory.SeedUserAsync("v-agency", "AgencyOwner");
        var mine = await _factory.SeedPropertyAsync(owner.Id, "إعلاني");
        var theirs = await _factory.SeedPropertyAsync(other.Id, "إعلان غيري");
        await _factory.InScopeAsync(async s =>
        {
            var repo = s.GetRequiredService<IListingViewRepository>();
            await repo.TryRecordAsync(ListingKind.Property, mine, new string('1', 64), 1, DateTime.UtcNow);
            await repo.TryRecordAsync(ListingKind.Property, mine, new string('2', 64), 1, DateTime.UtcNow.AddDays(-10));
            await repo.TryRecordAsync(ListingKind.Property, theirs, new string('3', 64), 1, DateTime.UtcNow);
        });

        var ownerClient = _factory.AuthedClient(owner.AccessToken);
        var stats = await Json(await ownerClient.GetAsync("/api/listing-views/stats"));
        var item = Assert.Single(stats.GetProperty("items").EnumerateArray());
        Assert.Equal(mine, item.GetProperty("listingId").GetGuid());
        Assert.Equal(2, item.GetProperty("totalViews").GetInt64());
        Assert.Equal(1, item.GetProperty("views7Days").GetInt32());
        Assert.Equal(2, item.GetProperty("views30Days").GetInt32());
        Assert.Equal(2, item.GetProperty("uniqueViewers30Days").GetInt32());

        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/listing-views/stats")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ownerClient.GetAsync("/api/listing-views/stats?scope=agency")).StatusCode);
        // An agency owner role without an agency membership has nothing to see either.
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.AuthedClient(agencyOwner.AccessToken).GetAsync("/api/listing-views/stats?scope=agency")).StatusCode);

        // Trend: own listing yes (30 zero-filled days), somebody else's no, unknown is 404.
        var trend = await Json(await ownerClient.GetAsync($"/api/listing-views/property/{mine}/trend?days=30"));
        Assert.Equal(30, trend.GetProperty("points").GetArrayLength());
        Assert.Equal(2, trend.GetProperty("points").EnumerateArray().Sum(p => p.GetProperty("views").GetInt32()));
        Assert.Equal(HttpStatusCode.Forbidden, (await ownerClient.GetAsync($"/api/listing-views/property/{theirs}/trend")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ownerClient.GetAsync($"/api/listing-views/property/{Guid.NewGuid()}/trend")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ownerClient.GetAsync($"/api/listing-views/villa/{mine}/trend")).StatusCode);
    }

    [Fact(DisplayName = "stats: an agency owner sees every listing published under the agency, an agent does not")]
    public async Task AgencyOwner_SeesTheAgencysListings()
    {
        var agencyOwner = await _factory.SeedUserAsync("v-agency", "AgencyOwner");
        var agent = await _factory.SeedUserAsync("v-agent", "AgencyAgent");
        var agentListing = await _factory.SeedPropertyAsync(agent.Id, "إعلان الوسيط");
        await _factory.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            var agency = Agency.Create("Office", $"office-{Guid.NewGuid():N}", agencyOwner.Id, "SY", now);
            db.Agencies.Add(agency);
            await db.SaveChangesAsync();
            (await db.UserAccounts.SingleAsync(a => a.Id == agencyOwner.Id)).JoinAgency(agency.Id, now);
            (await db.UserAccounts.SingleAsync(a => a.Id == agent.Id)).JoinAgency(agency.Id, now);
            (await db.Properties.SingleAsync(p => p.Id == agentListing)).SetAgency(agency.Id);
            await db.SaveChangesAsync();
            await s.GetRequiredService<IListingViewRepository>()
                .TryRecordAsync(ListingKind.Property, agentListing, new string('4', 64), 1, now);
        });

        var asOwner = await Json(await _factory.AuthedClient(agencyOwner.AccessToken).GetAsync("/api/listing-views/stats?scope=agency"));
        Assert.Equal("agency", asOwner.GetProperty("scope").GetString());
        Assert.Equal(1, asOwner.GetProperty("totalViews").GetInt64());
        Assert.Equal(agentListing, Assert.Single(asOwner.GetProperty("items").EnumerateArray()).GetProperty("listingId").GetGuid());
        Assert.Equal(HttpStatusCode.OK,
            (await _factory.AuthedClient(agencyOwner.AccessToken).GetAsync($"/api/listing-views/property/{agentListing}/trend")).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.AuthedClient(agent.AccessToken).GetAsync("/api/listing-views/stats?scope=agency")).StatusCode);
    }

    // ── Retention and schema ─────────────────────────────────────────────

    [Fact(DisplayName = "retention: old ledger rows are purged, recent ones and the running total stay")]
    public async Task Retention_PurgesHistory_ButNeverLowersTheTotal()
    {
        var owner = await _factory.SeedUserAsync("v-owner");
        var id = await _factory.SeedPropertyAsync(owner.Id);
        await _factory.InScopeAsync(async s =>
        {
            var repo = s.GetRequiredService<IListingViewRepository>();
            await repo.TryRecordAsync(ListingKind.Property, id, new string('5', 64), 1, DateTime.UtcNow.AddDays(-120));
            await repo.TryRecordAsync(ListingKind.Property, id, new string('6', 64), 1, DateTime.UtcNow.AddDays(-5));
        });

        var service = new ListingViewRetentionHostedService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Services.GetRequiredService<ILogger<ListingViewRetentionHostedService>>());
        Assert.Equal(1, await service.RunOnceAsync(CancellationToken.None));
        Assert.Equal(0, await service.RunOnceAsync(CancellationToken.None));

        await _factory.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.ListingViewEvents.CountAsync(e => e.ListingId == id));
        });
        Assert.Equal(2, await StoredCountAsync(ListingKind.Property, id));
    }

    [Fact(DisplayName = "schema: the dedupe key is a unique index and the counter cannot go negative")]
    public async Task Schema_EnforcesDedupeAndNonNegativeCounter()
    {
        await _factory.InScopeAsync(async s =>
        {
            var db = s.GetRequiredService<AppDbContext>();
            var indexes = await db.Database.SqlQueryRaw<string>(
                "SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'ListingViewEvents' AND indexname = 'UX_ListingViewEvents_Dedupe'").ToListAsync();
            Assert.Contains("UNIQUE", Assert.Single(indexes));

            await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync(
                "INSERT INTO \"ListingViewCounters\" (\"Kind\", \"ListingId\", \"ViewCount\", \"UpdatedAt\") VALUES ('Property', gen_random_uuid(), -1, now())"));
        });
    }
}

/// <summary>Own factory (and so its own in-memory limiter): the limit is per client and would otherwise be shared with the tests above.</summary>
[Trait("Feature", "Listings")]
public sealed class ListingViewRateLimitTests : IClassFixture<MessagingApiTestFactory>, IAsyncLifetime
{
    private readonly MessagingApiTestFactory _factory;

    public ListingViewRateLimitTests(MessagingApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(DisplayName = "views: the endpoint is rate limited (30 per minute per client), even when every request is a new visitor")]
    public async Task ScriptedFlood_IsRateLimited()
    {
        await RateLimitWindowSync.WaitForSafeWindowStartAsync(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(10));
        var owner = await _factory.SeedUserAsync("v-owner");
        var id = await _factory.SeedPropertyAsync(owner.Id);
        var client = _factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 40; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/api/properties/{id}/views");
            request.Headers.Add("X-Visitor-Id", $"flood-visitor-{i:D4}-xxxxxxxx");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 Chrome/120");
            statuses.Add((await client.SendAsync(request)).StatusCode);
        }

        Assert.Equal(30, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(10, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }
}
