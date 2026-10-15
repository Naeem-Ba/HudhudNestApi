using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Marketing.Entities;
using HudhudNestApi.Domain.Marketing.Enums;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.AppUpdates;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Marketing;

/// <summary>
/// The marketing-offer lifecycle through the real HTTP pipeline against PostgreSQL: create a
/// draft, activate it, see it (and only it) on the public endpoint, pause / end it, and every
/// refusal in between. This is the regression suite for the incident where activating an offer
/// from the admin dashboard answered 415 Unsupported Media Type because the client sent the
/// status as a bare string (text/plain) and the endpoint binds a JSON body.
///
/// Reuses <see cref="AppUpdateApiTestFactory"/> only for its real-Postgres host, real token
/// minting and per-test table truncation; nothing here touches app releases.
/// </summary>
[Trait("Feature", "MarketingOffers")]
[Trait("Category", "Integration")]
public sealed class OfferLifecycleHttpTests : IClassFixture<AppUpdateApiTestFactory>, IAsyncLifetime
{
    private readonly AppUpdateApiTestFactory _factory;

    public OfferLifecycleHttpTests(AppUpdateApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpClient> AdminClientAsync()
    {
        var admin = await _factory.SeedUserAsync("offers-admin", RoleNames.Admin);
        return _factory.AuthedClient(admin.AccessToken);
    }

    private static object OfferPayload(
        string name = "عرض تجريبي — أول 100 مكتب",
        DateTime? startsAtUtc = null,
        DateTime? endsAtUtc = null,
        int? maxRedemptions = 100) => new
        {
            name,
            description = "خصم للمكاتب العقارية المنضمة مبكرًا",
            discountType = "Percentage",
            discountValue = 25,
            startsAtUtc = startsAtUtc ?? DateTime.UtcNow.AddMinutes(-5),
            endsAtUtc = endsAtUtc ?? DateTime.UtcNow.AddDays(30),
            maxRedemptions,
            terms = "للمكاتب الجديدة فقط",
        };

    private static async Task<Guid> CreateDraftAsync(HttpClient admin, object? payload = null)
    {
        var response = await admin.PostAsJsonAsync("/api/offers", payload ?? OfferPayload());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> SetStatusAsync(HttpClient client, Guid id, string status) =>
        client.PutAsJsonAsync($"/api/offers/{id}/status", new { status });

    private async Task<OfferStatus> StatusInDatabaseAsync(Guid id) =>
        await _factory.InScopeAsync(async sp =>
            (await sp.GetRequiredService<AppDbContext>().Offers.AsNoTracking().SingleAsync(o => o.Id == id)).Status);

    private async Task<HttpResponseMessage> PublicActiveAsync() =>
        await _factory.CreateClient().GetAsync("/api/offers/active");

    // ── the incident ────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Activate with a JSON body returns 200, persists Active and is visible publicly")]
    public async Task Activate_WithJsonBody_PersistsAndShowsPublicly()
    {
        var admin = await AdminClientAsync();
        var id = await CreateDraftAsync(admin);

        Assert.Equal(HttpStatusCode.NoContent, (await PublicActiveAsync()).StatusCode); // draft is invisible

        var response = await SetStatusAsync(admin, id, "Active");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Active", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("isPubliclyVisible").GetBoolean());
        Assert.Equal(OfferStatus.Active, await StatusInDatabaseAsync(id));

        var publicResponse = await PublicActiveAsync();
        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        var publicBody = await publicResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, publicBody.GetProperty("id").GetGuid());
    }

    [Fact(DisplayName = "A bare-string text/plain status body is refused with 415 and changes nothing")]
    public async Task Activate_WithTextPlainBareString_Returns415AndLeavesDraft()
    {
        var admin = await AdminClientAsync();
        var id = await CreateDraftAsync(admin);

        // Exactly what Angular's HttpClient sends for put(url, 'Active'): the raw text, text/plain.
        var legacy = new StringContent("Active", Encoding.UTF8, "text/plain");
        var response = await admin.PutAsync($"/api/offers/{id}/status", legacy);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(OfferStatus.Draft, await StatusInDatabaseAsync(id));
    }

    [Fact(DisplayName = "A quoted JSON string is not the contract any more: 400, nothing changes")]
    public async Task Activate_WithBareJsonString_IsRejected()
    {
        var admin = await AdminClientAsync();
        var id = await CreateDraftAsync(admin);

        var response = await admin.PutAsync(
            $"/api/offers/{id}/status", new StringContent("\"Active\"", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OfferStatus.Draft, await StatusInDatabaseAsync(id));
    }

    [Fact(DisplayName = "Creating an offer with a non-JSON content type is refused with 415")]
    public async Task Create_WithTextPlain_Returns415()
    {
        var admin = await AdminClientAsync();

        var response = await admin.PostAsync(
            "/api/offers", new StringContent("name=x", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    // ── draft → active → paused → ended ─────────────────────────────────────────────────────

    [Fact(DisplayName = "A new offer is a Draft, hidden from the public and flagged not visible to the admin")]
    public async Task CreatedOffer_IsDraftAndHidden()
    {
        var admin = await AdminClientAsync();
        var id = await CreateDraftAsync(admin);

        var list = await (await admin.GetAsync("/api/offers")).Content.ReadFromJsonAsync<JsonElement>();
        var row = list.EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == id);

        Assert.Equal("Draft", row.GetProperty("status").GetString());
        Assert.False(row.GetProperty("isPubliclyVisible").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await PublicActiveAsync()).StatusCode);
    }

    [Fact(DisplayName = "Repeating Activate is idempotent: 200 both times, one audit entry")]
    public async Task Activate_Twice_IsIdempotent()
    {
        var admin = await AdminClientAsync();
        var id = await CreateDraftAsync(admin);

        Assert.Equal(HttpStatusCode.OK, (await SetStatusAsync(admin, id, "Active")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetStatusAsync(admin, id, "Active")).StatusCode);

        Assert.Equal(OfferStatus.Active, await StatusInDatabaseAsync(id));
        var audited = await _factory.InScopeAsync(async sp =>
            await sp.GetRequiredService<AppDbContext>().AuditLogs
                .CountAsync(a => a.Action == AuditActions.OfferStatusChangedByAdmin));
        Assert.Equal(1, audited);
    }

    [Fact(DisplayName = "Pause hides the offer, re-activate shows it, End is terminal")]
    public async Task PauseResumeEnd_FollowsTheLifecycle()
    {
        var admin = await AdminClientAsync();
        var id = await CreateDraftAsync(admin);
        await SetStatusAsync(admin, id, "Active");

        Assert.Equal(HttpStatusCode.OK, (await SetStatusAsync(admin, id, "Paused")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await PublicActiveAsync()).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await SetStatusAsync(admin, id, "Active")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PublicActiveAsync()).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await SetStatusAsync(admin, id, "Ended")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await PublicActiveAsync()).StatusCode);

        var reactivate = await SetStatusAsync(admin, id, "Active");
        Assert.Equal(HttpStatusCode.BadRequest, reactivate.StatusCode);
        Assert.Equal(OfferStatus.Ended, await StatusInDatabaseAsync(id));
    }

    [Fact(DisplayName = "Pausing a Draft is refused with 400 and an Arabic message")]
    public async Task Pause_FromDraft_Returns400()
    {
        var admin = await AdminClientAsync();
        var id = await CreateDraftAsync(admin);

        var response = await SetStatusAsync(admin, id, "Paused");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        Assert.Equal(OfferStatus.Draft, await StatusInDatabaseAsync(id));
    }

    [Theory(DisplayName = "Unknown, Draft or missing statuses are 400")]
    [InlineData("Draft")]
    [InlineData("Published")]
    [InlineData("")]
    public async Task SetStatus_WithInvalidValue_Returns400(string status)
    {
        var admin = await AdminClientAsync();
        var id = await CreateDraftAsync(admin);

        var response = await SetStatusAsync(admin, id, status);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OfferStatus.Draft, await StatusInDatabaseAsync(id));
    }

    [Fact(DisplayName = "A body without a status property is 400")]
    public async Task SetStatus_WithEmptyObject_Returns400()
    {
        var admin = await AdminClientAsync();
        var id = await CreateDraftAsync(admin);

        var response = await admin.PutAsJsonAsync($"/api/offers/{id}/status", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "Unknown offer id is 404")]
    public async Task SetStatus_UnknownId_Returns404()
    {
        var admin = await AdminClientAsync();

        var response = await SetStatusAsync(admin, Guid.NewGuid(), "Active");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── business rules on activation / public visibility ───────────────────────────────────

    [Fact(DisplayName = "An offer whose end date has passed cannot be activated")]
    public async Task Activate_ExpiredOffer_Returns400()
    {
        var admin = await AdminClientAsync();
        var id = await _factory.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<AppDbContext>();
            var offer = Offer.Create(
                "عرض منتهٍ", OfferDiscountType.Percentage, 10m,
                startsAtUtc: DateTime.UtcNow.AddDays(-10), endsAtUtc: DateTime.UtcNow.AddDays(-1));
            db.Offers.Add(offer);
            await db.SaveChangesAsync();
            return offer.Id;
        });

        var response = await SetStatusAsync(admin, id, "Active");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(OfferStatus.Draft, await StatusInDatabaseAsync(id));
        Assert.Equal(HttpStatusCode.NoContent, (await PublicActiveAsync()).StatusCode);
    }

    [Fact(DisplayName = "An offer that starts in the future activates but stays hidden until then")]
    public async Task Activate_FutureStart_IsActiveButNotPublic()
    {
        var admin = await AdminClientAsync();
        var id = await CreateDraftAsync(admin, OfferPayload(
            startsAtUtc: DateTime.UtcNow.AddDays(3), endsAtUtc: DateTime.UtcNow.AddDays(30)));

        var response = await SetStatusAsync(admin, id, "Active");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Active", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("isPubliclyVisible").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await PublicActiveAsync()).StatusCode);
    }

    [Fact(DisplayName = "Public offer payload never exposes lifecycle or internal fields")]
    public async Task PublicActive_ExposesOnlyThePublicShape()
    {
        var admin = await AdminClientAsync();
        var id = await CreateDraftAsync(admin);
        await SetStatusAsync(admin, id, "Active");

        var body = await (await PublicActiveAsync()).Content.ReadFromJsonAsync<JsonElement>();
        var keys = body.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();

        Assert.Equal(
            new[]
            {
                "description", "discountType", "discountValue", "endsAtUtc", "id", "maxRedemptions",
                "name", "redeemedCount", "targetPlanTier", "terms",
            },
            keys);
    }

    [Fact(DisplayName = "Creating, editing and changing status write audit entries")]
    public async Task AdminOperations_AreAudited()
    {
        var admin = await AdminClientAsync();
        var id = await CreateDraftAsync(admin);
        var update = await admin.PutAsJsonAsync($"/api/offers/{id}", OfferPayload(name: "اسم معدّل"));
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        await SetStatusAsync(admin, id, "Active");

        var actions = await _factory.InScopeAsync(async sp =>
            await sp.GetRequiredService<AppDbContext>().AuditLogs
                .Where(a => a.Action.StartsWith("Offer"))
                .Select(a => a.Action)
                .ToListAsync());

        Assert.Contains(AuditActions.OfferCreatedByAdmin, actions);
        Assert.Contains(AuditActions.OfferUpdatedByAdmin, actions);
        Assert.Contains(AuditActions.OfferStatusChangedByAdmin, actions);
    }

    // ── authorization ──────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Anonymous callers get 401 on every admin offer endpoint")]
    public async Task AdminEndpoints_RequireAuthentication()
    {
        var anonymous = _factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/offers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/offers", OfferPayload())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync($"/api/offers/{id}", OfferPayload())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SetStatusAsync(anonymous, id, "Active")).StatusCode);
    }

    [Fact(DisplayName = "A signed-in non-admin cannot change an offer's status")]
    public async Task NonAdmin_CannotChangeStatus()
    {
        var admin = await AdminClientAsync();
        var id = await CreateDraftAsync(admin);
        var user = await _factory.SeedUserAsync("offers-plain-user");
        var plain = _factory.AuthedClient(user.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await SetStatusAsync(plain, id, "Active")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.GetAsync("/api/offers")).StatusCode);
        Assert.Equal(OfferStatus.Draft, await StatusInDatabaseAsync(id));
    }
}
