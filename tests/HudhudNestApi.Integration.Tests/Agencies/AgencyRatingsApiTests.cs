using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Bookings.Entities;
using HudhudNestApi.Domain.Messaging.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Agencies;

/// <summary>
/// Office (agency) ratings over real HTTP and real PostgreSQL: the three endpoints the app already calls
/// (public list, eligibility, submit), the server-side rules (owner and members cannot rate, a real
/// interaction is required, one row per rater that is edited in place, 1..5 scores, 1000-character comment)
/// and the database guarantees (unique index under concurrency).
/// </summary>
public sealed class AgencyRatingsApiTests : IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory = new();

    private SeededUser _owner = null!;
    private SeededUser _member = null!;
    private SeededUser _customer = null!;
    private Guid _agencyId;
    private Guid _agencyPropertyId;

    private HttpClient Anonymous => _factory.CreateClient();
    private HttpClient As(SeededUser user) => _factory.AuthedClient(user.AccessToken);

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        _owner = await _factory.SeedUserAsync("agency-owner");
        _member = await _factory.SeedUserAsync("agency-member");
        _customer = await _factory.SeedUserAsync("agency-customer");

        (_agencyId, _agencyPropertyId) = await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            var agency = Agency.Create("مكتب الشام", $"sham-{Guid.NewGuid():N}", _owner.Id, "SY", now);
            db.Agencies.Add(agency);
            await db.SaveChangesAsync();

            var member = await db.UserAccounts.SingleAsync(a => a.Id == _member.Id);
            member.JoinAgency(agency.Id, now);

            var property = HudhudNestApi.Domain.Listings.Entities.Property.Create(
                $"Agency listing {Guid.NewGuid():N}", "desc", _owner.Id, HudhudNestApi.Domain.Enums.ListingType.ForSale);
            property.SetAgency(agency.Id);
            db.Properties.Add(property);
            await db.SaveChangesAsync();
            return (agency.Id, property.Id);
        });
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static object Body(int s = 5, int p = 4, int r = 4, int o = 5, string? comment = "ممتاز")
        => new { Service = s, Professionalism = p, Reliability = r, OverallExperience = o, Comment = comment };

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {text}");
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement;
    }

    private Task GiveCompletedVisitAsync(SeededUser visitor) => _factory.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<AppDbContext>();
        var visit = VisitRequest.Create(_agencyPropertyId, visitor.Id, DateTime.UtcNow.AddDays(1), "زائر", "+963900000000");
        visit.Confirm();
        visit.Complete();
        db.VisitRequests.Add(visit);
        await db.SaveChangesAsync();
    });

    private Task GiveMessageAsync(SeededUser sender) => _factory.InScopeAsync(async services =>
    {
        var db = services.GetRequiredService<AppDbContext>();
        db.Messages.Add(new Message { SenderId = sender.Id, ReceiverId = _owner.Id, PropertyId = _agencyPropertyId, Content = "هل ما زال متاحًا؟" });
        await db.SaveChangesAsync();
    });

    private Task<int> RowCountAsync() => _factory.InScopeAsync(async services =>
        await services.GetRequiredService<AppDbContext>().AgencyRatings.IgnoreQueryFilters().CountAsync(r => r.AgencyId == _agencyId));

    // ── Public read ──────────────────────────────────────────────

    [Fact]
    public async Task PublicList_NeedsNoToken_AndIsEmptyForAFreshOffice()
    {
        var root = await Json(await Anonymous.GetAsync($"/api/agencies/{_agencyId}/ratings"), HttpStatusCode.OK);

        Assert.Equal(0, root.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, root.GetProperty("averageOverall").GetDouble());
        Assert.Equal(1, root.GetProperty("page").GetInt32());
        Assert.Empty(root.GetProperty("ratings").EnumerateArray());
    }

    [Fact]
    public async Task UnknownOrDeactivatedOffice_IsNotFound_OnAllThreeEndpoints()
    {
        var unknown = Guid.NewGuid();
        await Json(await Anonymous.GetAsync($"/api/agencies/{unknown}/ratings"), HttpStatusCode.NotFound);
        await Json(await As(_customer).GetAsync($"/api/agencies/{unknown}/ratings/eligibility"), HttpStatusCode.NotFound);
        await Json(await As(_customer).PostAsJsonAsync($"/api/agencies/{unknown}/ratings", Body()), HttpStatusCode.NotFound);

        await GiveCompletedVisitAsync(_customer);
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            await db.Agencies.Where(a => a.Id == _agencyId).ExecuteUpdateAsync(s => s.SetProperty(a => a.IsActive, false));
        });

        await Json(await Anonymous.GetAsync($"/api/agencies/{_agencyId}/ratings"), HttpStatusCode.NotFound);
        await Json(await As(_customer).GetAsync($"/api/agencies/{_agencyId}/ratings/eligibility"), HttpStatusCode.NotFound);
        await Json(await As(_customer).PostAsJsonAsync($"/api/agencies/{_agencyId}/ratings", Body()), HttpStatusCode.NotFound);
    }

    // ── Authentication ───────────────────────────────────────────

    [Fact]
    public async Task EligibilityAndSubmit_RequireSignIn()
    {
        await Json(await Anonymous.GetAsync($"/api/agencies/{_agencyId}/ratings/eligibility"), HttpStatusCode.Unauthorized);
        await Json(await Anonymous.PostAsJsonAsync($"/api/agencies/{_agencyId}/ratings", Body()), HttpStatusCode.Unauthorized);
    }

    // ── Who may rate ─────────────────────────────────────────────

    [Fact]
    public async Task OwnerAndMember_CannotRate_EvenWithAnInteraction()
    {
        await GiveCompletedVisitAsync(_owner);
        await GiveMessageAsync(_member);

        foreach (var user in new[] { _owner, _member })
        {
            var eligibility = await Json(await As(user).GetAsync($"/api/agencies/{_agencyId}/ratings/eligibility"), HttpStatusCode.OK);
            Assert.False(eligibility.GetProperty("canRate").GetBoolean());
            Assert.True(eligibility.GetProperty("isMemberOrOwner").GetBoolean());

            await Json(await As(user).PostAsJsonAsync($"/api/agencies/{_agencyId}/ratings", Body()), HttpStatusCode.Forbidden);
        }

        Assert.Equal(0, await RowCountAsync());
    }

    [Fact]
    public async Task WithoutInteraction_CannotRate_AndEligibilityExplainsWhy()
    {
        var eligibility = await Json(await As(_customer).GetAsync($"/api/agencies/{_agencyId}/ratings/eligibility"), HttpStatusCode.OK);
        Assert.False(eligibility.GetProperty("canRate").GetBoolean());
        Assert.False(eligibility.GetProperty("isMemberOrOwner").GetBoolean());
        Assert.False(eligibility.GetProperty("hasInteraction").GetBoolean());
        Assert.False(eligibility.GetProperty("alreadyRated").GetBoolean());

        await Json(await As(_customer).PostAsJsonAsync($"/api/agencies/{_agencyId}/ratings", Body()), HttpStatusCode.Forbidden);
        Assert.Equal(0, await RowCountAsync());
    }

    [Fact]
    public async Task AnUnfinishedVisit_IsNotAnInteraction_ButACompletedOneIs()
    {
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var pending = VisitRequest.Create(_agencyPropertyId, _customer.Id, DateTime.UtcNow.AddDays(1), "زائر", "+963900000000");
            db.VisitRequests.Add(pending);
            await db.SaveChangesAsync();
        });
        var before = await Json(await As(_customer).GetAsync($"/api/agencies/{_agencyId}/ratings/eligibility"), HttpStatusCode.OK);
        Assert.False(before.GetProperty("hasInteraction").GetBoolean());

        await GiveCompletedVisitAsync(_customer);
        var after = await Json(await As(_customer).GetAsync($"/api/agencies/{_agencyId}/ratings/eligibility"), HttpStatusCode.OK);
        Assert.True(after.GetProperty("hasInteraction").GetBoolean());
        Assert.True(after.GetProperty("canRate").GetBoolean());
    }

    [Fact]
    public async Task AMessageAboutTheOfficesListing_IsAnInteraction_ButAMessageAboutAnotherListing_IsNot()
    {
        var elsewhere = await _factory.SeedPropertyAsync(_owner.Id); // a listing with no agency
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            db.Messages.Add(new Message { SenderId = _customer.Id, ReceiverId = _owner.Id, PropertyId = elsewhere, Content = "مرحبا" });
            await db.SaveChangesAsync();
        });
        var before = await Json(await As(_customer).GetAsync($"/api/agencies/{_agencyId}/ratings/eligibility"), HttpStatusCode.OK);
        Assert.False(before.GetProperty("hasInteraction").GetBoolean());

        await GiveMessageAsync(_customer);
        var after = await Json(await As(_customer).GetAsync($"/api/agencies/{_agencyId}/ratings/eligibility"), HttpStatusCode.OK);
        Assert.True(after.GetProperty("canRate").GetBoolean());
    }

    // ── Create, then edit in place ───────────────────────────────

    [Fact]
    public async Task Submit_CreatesThenEditsTheSameRow_AndTheListShowsIt()
    {
        await GiveCompletedVisitAsync(_customer);
        var client = As(_customer);

        var created = await Json(await client.PostAsJsonAsync($"/api/agencies/{_agencyId}/ratings", Body(5, 4, 4, 5, "ممتاز")), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(_customer.Id, created.GetProperty("raterId").GetGuid());
        Assert.Equal(4.5, created.GetProperty("overallScore").GetDouble());

        var edited = await Json(await client.PostAsJsonAsync($"/api/agencies/{_agencyId}/ratings", Body(1, 2, 3, 2, "تغيّر رأيي")), HttpStatusCode.OK);
        Assert.Equal(id, edited.GetProperty("id").GetGuid());
        Assert.Equal(1, edited.GetProperty("service").GetInt32());
        Assert.Equal(1, await RowCountAsync());

        var list = await Json(await Anonymous.GetAsync($"/api/agencies/{_agencyId}/ratings"), HttpStatusCode.OK);
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        var only = Assert.Single(list.GetProperty("ratings").EnumerateArray());
        Assert.Equal("تغيّر رأيي", only.GetProperty("comment").GetString());

        var eligibility = await Json(await client.GetAsync($"/api/agencies/{_agencyId}/ratings/eligibility"), HttpStatusCode.OK);
        Assert.True(eligibility.GetProperty("alreadyRated").GetBoolean());
        Assert.Equal(id, eligibility.GetProperty("existingRating").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task TheRaterComesFromTheToken_AFakeRaterIdInTheBodyIsIgnored()
    {
        await GiveCompletedVisitAsync(_customer);
        var response = await As(_customer).PostAsJsonAsync($"/api/agencies/{_agencyId}/ratings",
            new { Service = 3, Professionalism = 3, Reliability = 3, OverallExperience = 3, RaterId = _owner.Id });

        var created = await Json(response, HttpStatusCode.Created);
        Assert.Equal(_customer.Id, created.GetProperty("raterId").GetGuid());
    }

    // ── Validation ───────────────────────────────────────────────

    [Theory]
    [InlineData(0, 3, 3, 3)]
    [InlineData(6, 3, 3, 3)]
    [InlineData(3, -1, 3, 3)]
    [InlineData(3, 3, 3, 0)]
    public async Task ScoresOutsideOneToFive_Answer422_AndStoreNothing(int s, int p, int r, int o)
    {
        await GiveCompletedVisitAsync(_customer);

        await Json(await As(_customer).PostAsJsonAsync($"/api/agencies/{_agencyId}/ratings", Body(s, p, r, o)), HttpStatusCode.UnprocessableEntity);
        Assert.Equal(0, await RowCountAsync());
    }

    [Fact]
    public async Task CommentOverAThousandCharactersOrWithNul_Answers422_ExactlyAThousandIsAccepted()
    {
        await GiveCompletedVisitAsync(_customer);
        var client = As(_customer);

        await Json(await client.PostAsJsonAsync($"/api/agencies/{_agencyId}/ratings", Body(comment: new string('أ', 1001))), HttpStatusCode.UnprocessableEntity);
        await Json(await client.PostAsJsonAsync($"/api/agencies/{_agencyId}/ratings", Body(comment: "a\0b")), HttpStatusCode.UnprocessableEntity);
        await Json(await client.PostAsJsonAsync($"/api/agencies/{_agencyId}/ratings", Body(comment: new string('أ', 1000))), HttpStatusCode.Created);
    }

    [Fact]
    public async Task MissingScores_Answer422_NotA500()
    {
        await GiveCompletedVisitAsync(_customer);

        await Json(await As(_customer).PostAsJsonAsync($"/api/agencies/{_agencyId}/ratings", new { Comment = "فقط تعليق" }), HttpStatusCode.UnprocessableEntity);
    }

    // ── Concurrency ──────────────────────────────────────────────

    [Fact]
    public async Task TenParallelFirstSubmissions_LeaveExactlyOneRow_NoServerErrors()
    {
        await GiveCompletedVisitAsync(_customer);
        var client = As(_customer);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(i => client.PostAsJsonAsync($"/api/agencies/{_agencyId}/ratings", Body(s: 1 + i % 5))));

        var codes = responses.Select(r => (int)r.StatusCode).ToList();
        Assert.DoesNotContain(codes, c => c >= 500);
        Assert.Contains(201, codes);
        Assert.All(codes, c => Assert.Contains(c, new[] { 200, 201, 409 }));
        Assert.Equal(1, await RowCountAsync());
    }

    // ── Averages and paging ──────────────────────────────────────

    [Fact]
    public async Task Averages_AreComputedOverAllRaters_AndPagesDoNotOverlap()
    {
        var raters = new List<SeededUser>();
        for (var i = 0; i < 5; i++)
        {
            var rater = await _factory.SeedUserAsync($"rater{i}");
            raters.Add(rater);
            await GiveCompletedVisitAsync(rater);
        }

        // service: 5,5,4,4,2 -> 4.0; professionalism 4 for all; reliability 3 for all; experience 5,5,5,1,1 -> 3.4
        var service = new[] { 5, 5, 4, 4, 2 };
        var experience = new[] { 5, 5, 5, 1, 1 };
        for (var i = 0; i < raters.Count; i++)
            await Json(await As(raters[i]).PostAsJsonAsync($"/api/agencies/{_agencyId}/ratings", Body(service[i], 4, 3, experience[i], null)), HttpStatusCode.Created);

        var all = await Json(await Anonymous.GetAsync($"/api/agencies/{_agencyId}/ratings?page=1&pageSize=50"), HttpStatusCode.OK);
        Assert.Equal(5, all.GetProperty("totalCount").GetInt32());
        Assert.Equal(4.0, all.GetProperty("averageService").GetDouble());
        Assert.Equal(4.0, all.GetProperty("averageProfessionalism").GetDouble());
        Assert.Equal(3.0, all.GetProperty("averageReliability").GetDouble());
        Assert.Equal(3.4, all.GetProperty("averageOverallExperience").GetDouble());
        Assert.Equal(3.6, all.GetProperty("averageOverall").GetDouble());

        var first = await Json(await Anonymous.GetAsync($"/api/agencies/{_agencyId}/ratings?page=1&pageSize=2"), HttpStatusCode.OK);
        var second = await Json(await Anonymous.GetAsync($"/api/agencies/{_agencyId}/ratings?page=2&pageSize=2"), HttpStatusCode.OK);
        var third = await Json(await Anonymous.GetAsync($"/api/agencies/{_agencyId}/ratings?page=3&pageSize=2"), HttpStatusCode.OK);
        var ids = new[] { first, second, third }
            .SelectMany(p => p.GetProperty("ratings").EnumerateArray().Select(r => r.GetProperty("id").GetGuid())).ToList();

        Assert.Equal(new[] { 2, 2, 1 }, new[] { first, second, third }.Select(p => p.GetProperty("ratings").GetArrayLength()).ToArray());
        Assert.Equal(5, ids.Distinct().Count());
        Assert.Equal(5, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, first.GetProperty("pageSize").GetInt32());
    }

    [Theory]
    [InlineData("page=0&pageSize=10")]
    [InlineData("page=-3&pageSize=10")]
    [InlineData("page=1&pageSize=0")]
    [InlineData("page=1&pageSize=100000")]
    [InlineData("page=2147483647&pageSize=50")]
    public async Task OddPagingValues_NeverFail_AndAreClamped(string query)
    {
        var root = await Json(await Anonymous.GetAsync($"/api/agencies/{_agencyId}/ratings?{query}"), HttpStatusCode.OK);

        Assert.InRange(root.GetProperty("pageSize").GetInt32(), 1, 50);
        Assert.True(root.GetProperty("page").GetInt32() >= 1);
    }
}
