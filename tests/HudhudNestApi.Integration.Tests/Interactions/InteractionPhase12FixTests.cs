using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Interactions;

/// <summary>
/// QA Phase 12 regression tests over real HTTP and real Postgres: saved searches that failed at the database
/// as a 500, an unbounded number of saved searches, owner notes on a visit that overflowed their column, and
/// another user's unpublished listing leaking through favorites and messages.
/// </summary>
public sealed class InteractionPhase12FixTests : IAsyncLifetime
{
    private readonly InvestmentApiTestFactory _factory = new();
    private int _damascus;

    private SeededUser _owner = null!;
    private SeededUser _visitor = null!;
    private HttpClient OwnerClient => _factory.AuthedClient(_owner.AccessToken);
    private HttpClient VisitorClient => _factory.AuthedClient(_visitor.AccessToken);

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        await ClearLookupCacheAsync();
        _damascus = await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var damascus = Governorate.Create("دمشق", "Damascus", "SY", 1);
            db.Governorates.Add(damascus);
            await db.SaveChangesAsync();
            return damascus.Id;
        });
        _owner = await _factory.SeedUserAsync("owner");
        _visitor = await _factory.SeedUserAsync("visitor");
    }

    public async Task DisposeAsync()
    {
        await ClearLookupCacheAsync();
        _factory.Dispose();
    }

    /// <summary>
    /// PrepareDatabaseAsync empties the lookup tables, and the saved-search validator reads the property type
    /// catalog. Left cached, that empty catalog made the next test class (which creates a property with a seeded
    /// property type) fail with "Unknown property type", depending on the order the classes run in.
    /// </summary>
    private Task ClearLookupCacheAsync() => _factory.InScopeAsync(
        services => services.GetRequiredService<ICommonLookupService>().RefreshAllAsync());

    private Task<Guid> NewPropertyAsync(Guid ownerId, bool published) => _factory.InScopeAsync(async services =>
    {
        var id = await _factory.SeedPropertyAsync(ownerId);
        var db = services.GetRequiredService<AppDbContext>();
        await db.Properties.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPublished, published));
        return id;
    });

    private static async Task<string> Body(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {body}");
        return body;
    }

    // ── Favorites and messages on an unpublished listing (P12-D04) ──────────────────────────

    [Fact]
    public async Task Favorites_RefuseSomeoneElsesDraft_AcceptOwnDraftAndPublished_AndHideItOnceUnpublished()
    {
        var draft = await NewPropertyAsync(_owner.Id, published: false);
        var published = await NewPropertyAsync(_owner.Id, published: true);

        await Body(await VisitorClient.PostAsync($"/api/favorites/{draft}", null), HttpStatusCode.NotFound);
        await Body(await OwnerClient.PostAsync($"/api/favorites/{draft}", null), HttpStatusCode.Created);
        await Body(await VisitorClient.PostAsync($"/api/favorites/{published}", null), HttpStatusCode.Created);

        var visitorList = JsonDocument.Parse(await Body(await VisitorClient.GetAsync("/api/favorites"), HttpStatusCode.OK)).RootElement;
        Assert.Equal(published, Assert.Single(visitorList.EnumerateArray()).GetProperty("propertyId").GetGuid());

        // The owner switches the listing off after the visitor saved it: it must disappear from the list,
        // not keep showing its title and price.
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            await db.Properties.Where(p => p.Id == published).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPublished, false));
            return 0;
        });
        var after = JsonDocument.Parse(await Body(await VisitorClient.GetAsync("/api/favorites"), HttpStatusCode.OK)).RootElement;
        Assert.Empty(after.EnumerateArray());

        // The owner still sees their own unpublished listing in their favorites.
        var ownerList = JsonDocument.Parse(await Body(await OwnerClient.GetAsync("/api/favorites"), HttpStatusCode.OK)).RootElement;
        Assert.Contains(ownerList.EnumerateArray(), f => f.GetProperty("propertyId").GetGuid() == draft);
    }

    [Fact]
    public async Task Messages_ToSomeoneElsesDraft_AreNotFound_ButPublishedListingsAreReachable()
    {
        var draft = await NewPropertyAsync(_owner.Id, published: false);
        var published = await NewPropertyAsync(_owner.Id, published: true);

        await Body(await VisitorClient.PostAsJsonAsync("/api/messages", new { propertyId = draft, content = "hello there" }), HttpStatusCode.NotFound);
        await Body(await VisitorClient.PostAsJsonAsync("/api/messages", new { propertyId = published, content = "hello there" }), HttpStatusCode.Created);
    }

    // ── Saved searches (P12-D01, P12-D02) ────────────────────────────────────────────────

    [Theory]
    [InlineData("name")]
    [InlineData("city")]
    [InlineData("country")]
    [InlineData("governorate")]
    [InlineData("propertyType")]
    [InlineData("range")]
    public async Task SavedSearch_InvalidValues_Answer422_NotA500(string field)
    {
        var body = new Dictionary<string, object?> { ["city"] = "دمشق" };
        switch (field)
        {
            case "name": body["name"] = new string('ن', 5000); break;
            case "city": body["city"] = new string('c', 5000); break;
            case "country": body["countryCode"] = "SYR"; break;
            case "governorate": body["governorateId"] = 987654; break;
            case "propertyType": body["propertyTypeId"] = -3; break;
            case "range": body["minPrice"] = 900; body["maxPrice"] = 100; break;
        }

        await Body(await VisitorClient.PostAsJsonAsync("/api/savedsearch", body), HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task SavedSearch_ValidSearchWorks_AndTheTwentyFirstIsRefusedWith409()
    {
        await Body(await VisitorClient.PostAsJsonAsync("/api/savedsearch",
            new { name = "ok", governorateId = _damascus, minPrice = 100, maxPrice = 900 }), HttpStatusCode.Created);

        for (var i = 1; i < 20; i++)
            await Body(await VisitorClient.PostAsJsonAsync("/api/savedsearch", new { city = $"c{i}" }), HttpStatusCode.Created);

        var blocked = await Body(await VisitorClient.PostAsJsonAsync("/api/savedsearch", new { city = "one more" }), HttpStatusCode.Conflict);
        Assert.Contains("20", blocked);

        // The cap is per user: somebody else is not affected.
        await Body(await OwnerClient.PostAsJsonAsync("/api/savedsearch", new { city = "mine" }), HttpStatusCode.Created);

        // Deleting one frees a slot.
        var list = JsonDocument.Parse(await Body(await VisitorClient.GetAsync("/api/savedsearch"), HttpStatusCode.OK)).RootElement;
        var first = list.EnumerateArray().First().GetProperty("id").GetGuid();
        await Body(await VisitorClient.DeleteAsync($"/api/savedsearch/{first}"), HttpStatusCode.NoContent);

        // A deleted search is gone from the list too, not just from the cap (it used to stay listed and matched).
        var afterDelete = JsonDocument.Parse(await Body(await VisitorClient.GetAsync("/api/savedsearch"), HttpStatusCode.OK)).RootElement;
        Assert.Equal(19, afterDelete.GetArrayLength());
        Assert.DoesNotContain(afterDelete.EnumerateArray(), e => e.GetProperty("id").GetGuid() == first);
        await Body(await VisitorClient.PostAsJsonAsync("/api/savedsearch", new { city = "again" }), HttpStatusCode.Created);
    }

    // ── Owner note on a visit (P12-D03) ──────────────────────────────────────────────────

    [Theory]
    [InlineData("confirm")]
    [InlineData("decline")]
    [InlineData("propose-alternate")]
    public async Task VisitOwnerNote_Over500Characters_Answers422_AndAValidNoteStillWorks(string action)
    {
        var property = await NewPropertyAsync(_owner.Id, published: true);
        var requested = JsonDocument.Parse(await Body(await VisitorClient.PostAsJsonAsync("/api/visits", new
        {
            propertyId = property,
            proposedAt = DateTime.UtcNow.AddDays(3),
            visitorName = "Visitor",
            visitorPhone = "+963 944 123 456",
            visitorNote = "hi",
        }), HttpStatusCode.Created)).RootElement;
        var id = requested.GetProperty("id").GetGuid();

        object Payload(string note) => action == "propose-alternate"
            ? new { proposedAt = DateTime.UtcNow.AddDays(5), note }
            : new { note };

        await Body(await OwnerClient.PutAsJsonAsync($"/api/visits/{id}/{action}", Payload(new string('ن', 100_000))), HttpStatusCode.UnprocessableEntity);
        await Body(await OwnerClient.PutAsJsonAsync($"/api/visits/{id}/{action}", Payload("ok")), HttpStatusCode.NoContent);
    }

    // ── Public forms (P12-D10) ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ContactSubject_WithLineBreak_Answers422()
    {
        using var anonymous = _factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/contact", new
        {
            name = "Someone",
            email = "someone@example.test",
            subject = "hello\r\nBcc: x@example.test",
            message = "a message that is long enough",
        });
        await Body(response, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task LeadPhone_ThatIsNotANumber_Answers422()
    {
        using var anonymous = _factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/leads", new
        {
            fullName = "Someone",
            phone = "abcdefgh",
            city = "دمشق",
            userType = "renter",
        });
        await Body(response, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task SurveyEnum_ThatDoesNotExist_Answers422()
    {
        using var anonymous = _factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/surveys/landing", new { willingnessToPay = 99 });
        await Body(response, HttpStatusCode.UnprocessableEntity);
    }

    // ── Decisions on the open points (P12-D05, admin alert) ──────────────────────────────

    private async Task<Guid> RequestVisitAsync(Guid propertyId, HttpStatusCode expected = HttpStatusCode.Created)
    {
        var response = await VisitorClient.PostAsJsonAsync("/api/visits", new
        {
            propertyId,
            proposedAt = DateTime.UtcNow.AddDays(3),
            visitorName = "Visitor",
            visitorPhone = "+963 944 123 456",
            visitorNote = "hi",
        });
        var body = await Body(response, expected);
        return expected == HttpStatusCode.Created ? JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid() : Guid.Empty;
    }

    [Fact]
    public async Task CompleteVisit_BeforeItsDate_Answers400_AndWorksOnceTheDateHasPassed()
    {
        var property = await NewPropertyAsync(_owner.Id, published: true);
        var id = await RequestVisitAsync(property);
        await Body(await OwnerClient.PutAsJsonAsync($"/api/visits/{id}/confirm", new { note = "ok" }), HttpStatusCode.NoContent);

        await Body(await OwnerClient.PutAsync($"/api/visits/{id}/complete", null), HttpStatusCode.BadRequest);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            await db.VisitRequests.Where(v => v.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.ProposedAt, DateTime.UtcNow.AddHours(-2)));
            return 0;
        });

        await Body(await OwnerClient.PutAsync($"/api/visits/{id}/complete", null), HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData(PropertyStatus.Sold)]
    [InlineData(PropertyStatus.Rented)]
    public async Task RequestVisit_OnASoldOrRentedListing_Answers400(PropertyStatus status)
    {
        var property = await NewPropertyAsync(_owner.Id, published: true);
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            await db.Properties.Where(p => p.Id == property).ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, status));
            return 0;
        });

        await RequestVisitAsync(property, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ContactMessage_NotifiesEveryAdmin_OncePerMessage()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        using var anonymous = _factory.CreateClient();

        await Body(await anonymous.PostAsJsonAsync("/api/contact", new
        {
            name = "Someone",
            email = "someone@example.test",
            subject = "A question",
            message = "a message that is long enough",
        }), HttpStatusCode.OK);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var notes = await db.Notifications
                .Where(n => n.Type == NotificationType.ContactMessageReceived).ToListAsync();
            var note = Assert.Single(notes);
            Assert.Equal(admin.Id, note.RecipientId);
            Assert.NotNull(note.RelatedEntityId);
            Assert.DoesNotContain("someone@example.test", note.Message);
            return 0;
        });
    }
}
