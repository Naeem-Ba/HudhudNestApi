using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Services;

/// <summary>
/// "Which services are requested, were they delivered, and if not why": the contact reveal,
/// the provider's done / not-done answer, the customer's own answer (hidden from the provider),
/// per-service ratings and the admin statistics, over real HTTP and real Postgres.
/// </summary>
public sealed class ServiceOutcomeTrackingTests : IAsyncLifetime
{
    private const string ProviderPhone = "+963 11 777 4242";
    private const string ProviderEmail = "provider.contact@example.test";
    private const string SecretCustomerReason = "ProviderNoShow";

    private readonly InvestmentApiTestFactory _factory = new();
    private int _damascus;

    private SeededUser _admin = null!;
    private SeededUser _provider = null!;
    private SeededUser _customer = null!;
    private Guid _property;
    private Guid _offering;
    private HttpClient AdminClient => _factory.AuthedClient(_admin.AccessToken);
    private HttpClient ProviderClient => _factory.AuthedClient(_provider.AccessToken);
    private HttpClient CustomerClient => _factory.AuthedClient(_customer.AccessToken);

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        _damascus = await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var damascus = Governorate.Create("دمشق", "Damascus", "SY", 1);
            db.Governorates.Add(damascus);
            await db.SaveChangesAsync();
            return damascus.Id;
        });

        _admin = await _factory.SeedUserAsync("admin", "Admin");
        _provider = await _factory.SeedUserAsync("provider");
        _customer = await _factory.SeedUserAsync("customer");

        // Provider is approved through the real onboarding flow.
        var application = await ReadJson(await ProviderClient.PostAsJsonAsync("/api/ServiceProviderApplications", new
        {
            applicantType = "Company",
            displayName = "Damascus Verify Office",
            contactName = "Samir",
            contactEmail = "office@example.test",
            contactPhone = "+963 11 555 1234",
            categories = new[] { "Verification" },
            governorateIds = new[] { _damascus },
            experience = "Ten years of property document checks in Damascus.",
            companyName = "Damascus Verify LLC",
            licenseNumber = "LIC-123",
            termsAccepted = true,
        }), HttpStatusCode.Created);
        await ReadJson(await AdminClient.PostAsJsonAsync(
            $"/api/admin/service-providers/applications/{application.GetProperty("id").GetGuid()}/approve",
            new { approvedCategories = new[] { "Verification" }, approvedGovernorateIds = new[] { _damascus }, verificationLevel = "BusinessVerified", note = "ok" }),
            HttpStatusCode.OK);

        // The contact the customer must receive on request, and must never see in the ad.
        await ReadJson(await ProviderClient.PutAsJsonAsync("/api/ServiceProviders/me", new
        {
            displayName = "Damascus Verify Office",
            bio = "Document checks",
            contactEmail = ProviderEmail,
            contactPhone = ProviderPhone,
        }), HttpStatusCode.OK);

        var offering = await ReadJson(await ProviderClient.PostAsJsonAsync("/api/ServiceOfferings",
            new { category = "Verification", title = "Ownership document check", description = "Deed and registry check", estimatedDurationDays = 3 }),
            HttpStatusCode.Created);
        _offering = offering.GetProperty("id").GetGuid();

        _property = await _factory.InScopeAsync(async services =>
        {
            var id = await _factory.SeedPropertyAsync(_customer.Id);
            var db = services.GetRequiredService<AppDbContext>();
            await db.Properties.Where(p => p.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.GovernorateId, _damascus)
                .SetProperty(p => p.IsPublished, true));
            return id;
        });
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    // The API allows one live request per property and service, so each request gets its own property.
    private async Task<Guid> RequestAsync()
    {
        var property = await _factory.InScopeAsync(async services =>
        {
            var id = await _factory.SeedPropertyAsync(_customer.Id);
            var db = services.GetRequiredService<AppDbContext>();
            await db.Properties.Where(p => p.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.GovernorateId, _damascus)
                .SetProperty(p => p.IsPublished, true));
            return id;
        });
        var created = await ReadJson(await CustomerClient.PostAsJsonAsync("/api/ServiceRequests",
            new { propertyId = property, serviceOfferingId = _offering, requesterNote = "Please check the deed." }),
            HttpStatusCode.Created);
        return created.GetProperty("id").GetGuid();
    }

    private async Task<Guid> AcceptedAsync()
    {
        var id = await RequestAsync();
        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{id}/accept", new { providerNote = "ok" }), HttpStatusCode.OK);
        return id;
    }

    // ── contact reveal ───────────────────────────────────────────────

    [Fact(DisplayName = "Provider contact: hidden from the ad, revealed to the requester on request, never shown to the provider's inbox")]
    public async Task ProviderContact_RevealedOnlyToRequester()
    {
        var anonymous = _factory.CreateClient();
        var ad = await anonymous.GetStringAsync("/api/ServiceOfferings?category=Verification");
        Assert.DoesNotContain("777 4242", ad);
        Assert.DoesNotContain(ProviderEmail, ad);
        var adById = await anonymous.GetStringAsync($"/api/ServiceOfferings/{_offering}");
        Assert.DoesNotContain("777 4242", adById);

        var created = await ReadJson(await CustomerClient.PostAsJsonAsync("/api/ServiceRequests",
            new { propertyId = _property, serviceOfferingId = _offering }), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(ProviderPhone, created.GetProperty("providerContact").GetProperty("phone").GetString());
        Assert.Equal(ProviderEmail, created.GetProperty("providerContact").GetProperty("email").GetString());

        var mine = await ReadJson(await CustomerClient.GetAsync("/api/ServiceRequests/mine"), HttpStatusCode.OK);
        Assert.Equal(ProviderPhone, mine.EnumerateArray().Single().GetProperty("providerContact").GetProperty("phone").GetString());

        // The provider reads the same request: no contact block, no customer feedback fields filled.
        var inbox = await ProviderClient.GetStringAsync("/api/ServiceRequests/provider-inbox");
        Assert.DoesNotContain("777 4242", inbox);
        var asProvider = await ReadJson(await ProviderClient.GetAsync($"/api/ServiceRequests/{id}"), HttpStatusCode.OK);
        Assert.Equal(JsonValueKind.Null, asProvider.GetProperty("providerContact").ValueKind);

        var asCustomer = await ReadJson(await CustomerClient.GetAsync($"/api/ServiceRequests/{id}"), HttpStatusCode.OK);
        Assert.Equal(ProviderPhone, asCustomer.GetProperty("providerContact").GetProperty("phone").GetString());
    }

    [Fact(DisplayName = "Provider contact: disappears once the request is rejected")]
    public async Task ProviderContact_HiddenAfterReject()
    {
        var id = await RequestAsync();

        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{id}/reject",
            new { reason = "خارج منطقتنا", reasonCode = "OutsideCoverage" }), HttpStatusCode.OK);

        var asCustomer = await ReadJson(await CustomerClient.GetAsync($"/api/ServiceRequests/{id}"), HttpStatusCode.OK);
        Assert.Equal("Rejected", asCustomer.GetProperty("status").GetString());
        Assert.Equal("OutsideCoverage", asCustomer.GetProperty("outcomeReason").GetString());
        Assert.Equal(JsonValueKind.Null, asCustomer.GetProperty("providerContact").ValueKind);
    }

    // ── provider says done / not done ────────────────────────────────

    [Fact(DisplayName = "Not completed: provider closes an accepted request with a reason, requester is notified and loses the contact")]
    public async Task NotCompleted_FromAccepted()
    {
        var id = await AcceptedAsync();

        var closed = await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{id}/not-completed",
            new { reason = "CustomerUnreachable", note = "Phone off for three days." }), HttpStatusCode.OK);

        Assert.Equal("NotCompleted", closed.GetProperty("status").GetString());
        Assert.Equal("CustomerUnreachable", closed.GetProperty("outcomeReason").GetString());
        Assert.Equal(JsonValueKind.Null, closed.GetProperty("providerContact").ValueKind);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.Notifications.CountAsync(n =>
                n.RecipientId == _customer.Id && n.Type == NotificationType.ServiceRequestNotCompleted));
            Assert.True(await db.ServiceRequestStatusHistories.AnyAsync(h => h.ServiceRequestId == id));
        });

        // It cannot be cancelled or closed twice afterwards.
        Assert.Equal(HttpStatusCode.Conflict, (await CustomerClient.PutAsJsonAsync($"/api/ServiceRequests/{id}/cancel", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{id}/not-completed",
            new { reason = "CustomerUnreachable" })).StatusCode);
    }

    [Fact(DisplayName = "Not completed: validation, authorization and state guards")]
    public async Task NotCompleted_Guards()
    {
        var underReview = await RequestAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await ProviderClient.PutAsJsonAsync(
            $"/api/ServiceRequests/{underReview}/not-completed", new { reason = "CustomerUnreachable" })).StatusCode);

        var id = await AcceptedAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await ProviderClient.PutAsJsonAsync(
            $"/api/ServiceRequests/{id}/not-completed", new { reason = "Other" })).StatusCode); // note required
        Assert.Equal(HttpStatusCode.BadRequest, (await ProviderClient.PutAsJsonAsync(
            $"/api/ServiceRequests/{id}/not-completed", new { reason = "NoSuchReason" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CustomerClient.PutAsJsonAsync(
            $"/api/ServiceRequests/{id}/not-completed", new { reason = "CustomerUnreachable" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PutAsJsonAsync(
            $"/api/ServiceRequests/{id}/not-completed", new { reason = "CustomerUnreachable" })).StatusCode);
    }

    [Fact(DisplayName = "Complete: provider can close straight from Accepted")]
    public async Task Complete_FromAccepted()
    {
        var id = await AcceptedAsync();

        var done = await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{id}/complete", new { }), HttpStatusCode.OK);

        Assert.Equal("Completed", done.GetProperty("status").GetString());
    }

    // ── customer answers ─────────────────────────────────────────────

    [Fact(DisplayName = "Customer feedback: the reason reaches the admin but never the provider, and only once")]
    public async Task CustomerFeedback_HiddenFromProvider()
    {
        var id = await AcceptedAsync();
        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{id}/not-completed",
            new { reason = "CustomerNotAvailable" }), HttpStatusCode.OK);

        // A reason is mandatory here; a provider cannot answer for the customer.
        Assert.Equal(HttpStatusCode.BadRequest, (await CustomerClient.PutAsJsonAsync(
            $"/api/ServiceRequests/{id}/feedback", new { answer = "Confirmed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ProviderClient.PutAsJsonAsync(
            $"/api/ServiceRequests/{id}/feedback", new { answer = "Confirmed", reason = "ChangedMind" })).StatusCode);

        var answered = await ReadJson(await CustomerClient.PutAsJsonAsync($"/api/ServiceRequests/{id}/feedback",
            new { answer = "Disputed", reason = SecretCustomerReason, note = "Nobody called me." }), HttpStatusCode.OK);
        Assert.Equal("Disputed", answered.GetProperty("requesterAnswer").GetString());
        Assert.Equal(SecretCustomerReason, answered.GetProperty("requesterReason").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await CustomerClient.PutAsJsonAsync(
            $"/api/ServiceRequests/{id}/feedback", new { answer = "Confirmed", reason = "ChangedMind" })).StatusCode);

        // The provider sees none of it, neither in the request nor in its history.
        var inbox = await ProviderClient.GetStringAsync("/api/ServiceRequests/provider-inbox");
        Assert.DoesNotContain(SecretCustomerReason, inbox);
        Assert.DoesNotContain("Nobody called me", inbox);
        var asProvider = await ProviderClient.GetStringAsync($"/api/ServiceRequests/{id}");
        Assert.DoesNotContain(SecretCustomerReason, asProvider);
        var history = await ProviderClient.GetStringAsync($"/api/ServiceRequests/{id}/history");
        Assert.DoesNotContain(SecretCustomerReason, history);
        Assert.DoesNotContain("Nobody called me", history);

        // The admin sees the dispute and it is flagged for attention.
        var detail = await ReadJson(await AdminClient.GetAsync($"/api/admin/service-requests/{id}"), HttpStatusCode.OK);
        Assert.True(detail.GetProperty("needsAttention").GetBoolean());
        Assert.Equal(SecretCustomerReason, detail.GetProperty("request").GetProperty("requesterReason").GetString());
        Assert.Equal("Nobody called me.", detail.GetProperty("request").GetProperty("requesterFeedbackNote").GetString());
    }

    [Fact(DisplayName = "Customer feedback: another user cannot answer, and an open request cannot be answered")]
    public async Task CustomerFeedback_Guards()
    {
        var stranger = await _factory.SeedUserAsync("stranger");
        var id = await AcceptedAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await CustomerClient.PutAsJsonAsync(
            $"/api/ServiceRequests/{id}/feedback", new { answer = "Confirmed" })).StatusCode);

        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{id}/complete", new { }), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.AuthedClient(stranger.AccessToken).PutAsJsonAsync(
            $"/api/ServiceRequests/{id}/feedback", new { answer = "Confirmed" })).StatusCode);

        var ok = await ReadJson(await CustomerClient.PutAsJsonAsync($"/api/ServiceRequests/{id}/feedback",
            new { answer = "Confirmed" }), HttpStatusCode.OK);
        Assert.Equal("Confirmed", ok.GetProperty("requesterAnswer").GetString());
    }

    [Fact(DisplayName = "Cancel: the requester's reason is stored for the statistics")]
    public async Task Cancel_StoresRequesterReason()
    {
        var id = await RequestAsync();

        var cancelled = await ReadJson(await CustomerClient.PutAsJsonAsync($"/api/ServiceRequests/{id}/cancel",
            new { reason = "found someone else", requesterReason = "FoundOtherProvider" }), HttpStatusCode.OK);

        Assert.Equal("FoundOtherProvider", cancelled.GetProperty("requesterReason").GetString());
    }

    // ── ratings ──────────────────────────────────────────────────────

    [Fact(DisplayName = "Ratings: offering average counts visible reviews only, and an admin can hide and restore one")]
    public async Task Ratings_AverageExcludesHiddenReviews()
    {
        var first = await AcceptedAsync();
        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{first}/complete", new { }), HttpStatusCode.OK);
        var second = await AcceptedAsync();
        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{second}/complete", new { }), HttpStatusCode.OK);

        // Cannot review before the work is done.
        var open = await AcceptedAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await CustomerClient.PostAsJsonAsync($"/api/ServiceRequests/{open}/review", new { rating = 5 })).StatusCode);

        await ReadJson(await CustomerClient.PostAsJsonAsync($"/api/ServiceRequests/{first}/review", new { rating = 5, comment = "great" }), HttpStatusCode.Created);
        var low = await ReadJson(await CustomerClient.PostAsJsonAsync($"/api/ServiceRequests/{second}/review", new { rating = 1, comment = "rude" }), HttpStatusCode.Created);
        var lowReviewId = low.GetProperty("id").GetGuid();

        var anonymous = _factory.CreateClient();
        var offering = await ReadJson(await anonymous.GetAsync($"/api/ServiceOfferings/{_offering}"), HttpStatusCode.OK);
        Assert.Equal(3.0, offering.GetProperty("averageRating").GetDouble());
        Assert.Equal(2, offering.GetProperty("reviewCount").GetInt32());

        // Moderation needs a reason, an admin, and is reversible.
        Assert.Equal(HttpStatusCode.Forbidden, (await CustomerClient.PutAsJsonAsync(
            $"/api/admin/service-requests/reviews/{lowReviewId}/visibility", new { hidden = true, reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await AdminClient.PutAsJsonAsync(
            $"/api/admin/service-requests/reviews/{lowReviewId}/visibility", new { hidden = true })).StatusCode); // domain rule: a reason is required
        var hidden = await ReadJson(await AdminClient.PutAsJsonAsync(
            $"/api/admin/service-requests/reviews/{lowReviewId}/visibility", new { hidden = true, reason = "abusive wording" }), HttpStatusCode.OK);
        Assert.True(hidden.GetProperty("isHidden").GetBoolean());

        offering = await ReadJson(await anonymous.GetAsync($"/api/ServiceOfferings/{_offering}"), HttpStatusCode.OK);
        Assert.Equal(5.0, offering.GetProperty("averageRating").GetDouble());
        Assert.Equal(1, offering.GetProperty("reviewCount").GetInt32());

        await ReadJson(await AdminClient.PutAsJsonAsync(
            $"/api/admin/service-requests/reviews/{lowReviewId}/visibility", new { hidden = false }), HttpStatusCode.OK);
        offering = await ReadJson(await anonymous.GetAsync($"/api/ServiceOfferings/{_offering}"), HttpStatusCode.OK);
        Assert.Equal(2, offering.GetProperty("reviewCount").GetInt32());

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == AuditActions.ServiceReviewHiddenByAdmin));
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == AuditActions.ServiceReviewUnhiddenByAdmin));
        });
    }

    // ── admin ────────────────────────────────────────────────────────

    [Fact(DisplayName = "Admin: statistics show how often each service was requested, completed, failed and why")]
    public async Task Admin_Stats()
    {
        var done = await AcceptedAsync();
        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{done}/complete", new { }), HttpStatusCode.OK);
        var failed = await AcceptedAsync();
        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{failed}/not-completed",
            new { reason = "PriceNotAgreed" }), HttpStatusCode.OK);
        await ReadJson(await CustomerClient.PutAsJsonAsync($"/api/ServiceRequests/{failed}/feedback",
            new { answer = "Confirmed", reason = "PriceTooHigh" }), HttpStatusCode.OK);
        await RequestAsync(); // still open
        var cancelled = await RequestAsync();
        await ReadJson(await CustomerClient.PutAsJsonAsync($"/api/ServiceRequests/{cancelled}/cancel",
            new { requesterReason = "ChangedMind" }), HttpStatusCode.OK);

        var stats = await ReadJson(await AdminClient.GetAsync("/api/admin/service-requests/stats"), HttpStatusCode.OK);

        var totals = stats.GetProperty("totals");
        Assert.Equal(4, totals.GetProperty("requested").GetInt32());
        Assert.Equal(1, totals.GetProperty("open").GetInt32());
        Assert.Equal(1, totals.GetProperty("completed").GetInt32());
        Assert.Equal(1, totals.GetProperty("notCompleted").GetInt32());
        Assert.Equal(1, totals.GetProperty("cancelled").GetInt32());
        Assert.Equal(1.0 / 3, totals.GetProperty("completionRate").GetDouble(), 3);

        var offering = stats.GetProperty("byOffering").EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == _offering);
        Assert.Equal(4, offering.GetProperty("requested").GetInt32());
        Assert.Equal("Verification", stats.GetProperty("byCategory").EnumerateArray().Single(r => r.GetProperty("requested").GetInt32() == 4).GetProperty("key").GetString());
        Assert.Single(stats.GetProperty("byProvider").EnumerateArray());

        var providerReasons = stats.GetProperty("providerReasons").EnumerateArray().ToDictionary(r => r.GetProperty("reason").GetString()!, r => r.GetProperty("count").GetInt32());
        Assert.Equal(1, providerReasons["PriceNotAgreed"]);
        var requesterReasons = stats.GetProperty("requesterReasons").EnumerateArray().ToDictionary(r => r.GetProperty("reason").GetString()!, r => r.GetProperty("count").GetInt32());
        Assert.Equal(1, requesterReasons["PriceTooHigh"]);
        Assert.Equal(1, requesterReasons["ChangedMind"]);

        // A window in the past contains none of it; a reversed window is refused.
        var past = await ReadJson(await AdminClient.GetAsync("/api/admin/service-requests/stats?from=2020-01-01&to=2020-02-01"), HttpStatusCode.OK);
        Assert.Equal(0, past.GetProperty("totals").GetProperty("requested").GetInt32());
        var reversed = await AdminClient.GetAsync("/api/admin/service-requests/stats?from=2026-02-01&to=2026-01-01");
        Assert.True((int)reversed.StatusCode is >= 400 and < 500);
    }

    [Fact(DisplayName = "Admin: list filters by status, category, offering and search, with paging")]
    public async Task Admin_List_Filters()
    {
        var done = await AcceptedAsync();
        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{done}/complete", new { }), HttpStatusCode.OK);
        await RequestAsync();
        await RequestAsync();

        var all = await ReadJson(await AdminClient.GetAsync("/api/admin/service-requests"), HttpStatusCode.OK);
        Assert.Equal(3, all.GetProperty("total").GetInt32());

        var completed = await ReadJson(await AdminClient.GetAsync("/api/admin/service-requests?status=Completed"), HttpStatusCode.OK);
        Assert.Equal(1, completed.GetProperty("total").GetInt32());
        Assert.Equal(done, completed.GetProperty("items")[0].GetProperty("id").GetGuid());

        var byOffering = await ReadJson(await AdminClient.GetAsync($"/api/admin/service-requests?offeringId={_offering}&category=Verification"), HttpStatusCode.OK);
        Assert.Equal(3, byOffering.GetProperty("total").GetInt32());
        var otherCategory = await ReadJson(await AdminClient.GetAsync("/api/admin/service-requests?category=Cleaning"), HttpStatusCode.OK);
        Assert.Equal(0, otherCategory.GetProperty("total").GetInt32());

        var search = await ReadJson(await AdminClient.GetAsync("/api/admin/service-requests?search=Ownership%20document"), HttpStatusCode.OK);
        Assert.Equal(3, search.GetProperty("total").GetInt32());
        var miss = await ReadJson(await AdminClient.GetAsync("/api/admin/service-requests?search=zzzz%25"), HttpStatusCode.OK);
        Assert.Equal(0, miss.GetProperty("total").GetInt32());

        var paged = await ReadJson(await AdminClient.GetAsync("/api/admin/service-requests?page=2&pageSize=2"), HttpStatusCode.OK);
        Assert.Single(paged.GetProperty("items").EnumerateArray());
        Assert.Equal(3, paged.GetProperty("total").GetInt32());
    }

    [Fact(DisplayName = "Admin: sets the outcome of a stuck request, notifies the customer and audits it")]
    public async Task Admin_SetOutcome()
    {
        var stuck = await RequestAsync(); // provider never answered

        Assert.Equal(HttpStatusCode.BadRequest, (await AdminClient.PutAsJsonAsync(
            $"/api/admin/service-requests/{stuck}/outcome", new { completed = false, reason = "ProviderUnavailable" })).StatusCode); // admin note missing

        var detail = await ReadJson(await AdminClient.PutAsJsonAsync($"/api/admin/service-requests/{stuck}/outcome",
            new { completed = false, reason = "ProviderUnavailable", adminNote = "Provider did not answer for 10 days." }), HttpStatusCode.OK);

        Assert.Equal("NotCompleted", detail.GetProperty("request").GetProperty("status").GetString());
        Assert.Equal("ProviderUnavailable", detail.GetProperty("request").GetProperty("outcomeReason").GetString());
        Assert.Contains(detail.GetProperty("history").EnumerateArray(), h => (h.GetProperty("note").GetString() ?? "").Contains("قرار إداري"));

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.AuditLogs.CountAsync(a =>
                a.Action == AuditActions.ServiceRequestOutcomeSetByAdmin && a.UserId == _admin.Id));
            Assert.Equal(1, await db.Notifications.CountAsync(n =>
                n.RecipientId == _customer.Id && n.Type == NotificationType.ServiceRequestNotCompleted));
        });

        // Already closed: a second decision conflicts.
        Assert.Equal(HttpStatusCode.Conflict, (await AdminClient.PutAsJsonAsync($"/api/admin/service-requests/{stuck}/outcome",
            new { completed = true, adminNote = "again" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await AdminClient.PutAsJsonAsync($"/api/admin/service-requests/{Guid.NewGuid()}/outcome",
            new { completed = true, adminNote = "x" })).StatusCode);
    }

    [Fact(DisplayName = "Admin endpoints: anonymous gets 401, a customer and a provider get 403")]
    public async Task Admin_Authorization()
    {
        string[] urls =
        [
            "/api/admin/service-requests",
            "/api/admin/service-requests/stats",
            $"/api/admin/service-requests/{Guid.NewGuid()}",
        ];

        foreach (var url in urls)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await CustomerClient.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ProviderClient.GetAsync(url)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await AdminClient.GetAsync($"/api/admin/service-requests/{Guid.NewGuid()}")).StatusCode);
    }
}
