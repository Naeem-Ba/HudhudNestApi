using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Services;

/// <summary>
/// The conversation stage of a service request: "ask more" and "request" both open an inquiry,
/// provider and customer talk inside the app, the customer's confirmation is what makes it a request,
/// an unconfirmed inquiry is reminded once, and a month of silence expires it (kept, never deleted),
/// over real HTTP and real Postgres.
/// </summary>
public sealed class ServiceConversationTests : IAsyncLifetime
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

    private async Task<Guid> NewPropertyAsync() => await _factory.InScopeAsync(async services =>
    {
        var id = await _factory.SeedPropertyAsync(_customer.Id);
        var db = services.GetRequiredService<AppDbContext>();
        await db.Properties.Where(p => p.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.GovernorateId, _damascus)
            .SetProperty(p => p.IsPublished, true));
        return id;
    });

    private async Task<Guid> InquireAsync(string intent = "Inquire", string message = "كم السعر وكيف الدفع؟")
    {
        var created = await ReadJson(await CustomerClient.PostAsJsonAsync("/api/ServiceRequests",
            new { propertyId = await NewPropertyAsync(), serviceOfferingId = _offering, requesterNote = message, intent }),
            HttpStatusCode.Created);
        return created.GetProperty("id").GetGuid();
    }

    private async Task<Guid> ConfirmedAsync()
    {
        var id = await InquireAsync("Request");
        await ReadJson(await CustomerClient.PutAsync($"/api/ServiceRequests/{id}/confirm", null), HttpStatusCode.OK);
        return id;
    }

    private Task SweepAsync(DateTime now) => new ServiceRequestLifecycleHostedService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Services.GetRequiredService<ILogger<ServiceRequestLifecycleHostedService>>())
        .RunOnceAsync(CancellationToken.None, now);

    private async Task<int> NotificationsAsync(Guid userId, NotificationType type) => await _factory.InScopeAsync(async services =>
        await services.GetRequiredService<AppDbContext>().Notifications.CountAsync(n => n.RecipientId == userId && n.Type == type));

    // ── opening the conversation ─────────────────────────────────────

    [Theory(DisplayName = "Inquiry: both buttons open a conversation that is not yet a request")]
    [InlineData("Inquire")]
    [InlineData("Request")]
    public async Task Inquiry_IsNotARequestYet(string intent)
    {
        var id = await InquireAsync(intent);

        var mine = await ReadJson(await CustomerClient.GetAsync($"/api/ServiceRequests/{id}"), HttpStatusCode.OK);
        Assert.Equal("Inquiry", mine.GetProperty("status").GetString());
        Assert.Equal(intent, mine.GetProperty("intent").GetString());
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("confirmedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("providerContact").ValueKind);
        Assert.Equal(1, mine.GetProperty("messageCount").GetInt32());

        // The provider is told about the inquiry (not about a "request") and finds it in the inbox.
        Assert.Equal(1, await NotificationsAsync(_provider.Id, NotificationType.ServiceInquiryReceived));
        Assert.Equal(0, await NotificationsAsync(_provider.Id, NotificationType.ServiceRequestSubmitted));
        var inbox = await ReadJson(await ProviderClient.GetAsync("/api/ServiceRequests/provider-inbox"), HttpStatusCode.OK);
        var row = inbox.EnumerateArray().Single();
        Assert.Equal("Inquiry", row.GetProperty("status").GetString());
        Assert.Equal(1, row.GetProperty("unreadMessageCount").GetInt32());

        // Counted for the statistics as an inquiry, not as a request.
        var stats = await ReadJson(await AdminClient.GetAsync("/api/admin/service-requests/stats"), HttpStatusCode.OK);
        var totals = stats.GetProperty("totals");
        Assert.Equal(1, totals.GetProperty("inquiries").GetInt32());
        Assert.Equal(1, totals.GetProperty("openInquiries").GetInt32());
        Assert.Equal(0, totals.GetProperty("requested").GetInt32());
        Assert.Equal(0.0, totals.GetProperty("conversionRate").GetDouble());
    }

    [Fact(DisplayName = "Inquiry: the first message is required, and one live conversation per service and property")]
    public async Task Inquiry_Validation()
    {
        var property = await NewPropertyAsync();

        // Validation failures answer 422; an intent that is not one of the two buttons never binds (400).
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await CustomerClient.PostAsJsonAsync("/api/ServiceRequests",
            new { propertyId = property, serviceOfferingId = _offering, intent = "Inquire" })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await CustomerClient.PostAsJsonAsync("/api/ServiceRequests",
            new { propertyId = property, serviceOfferingId = _offering, requesterNote = new string('x', 1001), intent = "Inquire" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CustomerClient.PostAsJsonAsync("/api/ServiceRequests",
            new { propertyId = property, serviceOfferingId = _offering, requesterNote = "hi", intent = "Nonsense" })).StatusCode);

        await ReadJson(await CustomerClient.PostAsJsonAsync("/api/ServiceRequests",
            new { propertyId = property, serviceOfferingId = _offering, requesterNote = "hi", intent = "Inquire" }), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.Conflict, (await CustomerClient.PostAsJsonAsync("/api/ServiceRequests",
            new { propertyId = property, serviceOfferingId = _offering, requesterNote = "again", intent = "Request" })).StatusCode);
    }

    [Fact(DisplayName = "Legacy: a call without intent (an app from before this stage) is confirmed on the spot")]
    public async Task Legacy_NoIntent_IsConfirmedAtOnce()
    {
        var created = await ReadJson(await CustomerClient.PostAsJsonAsync("/api/ServiceRequests",
            new { propertyId = await NewPropertyAsync(), serviceOfferingId = _offering }), HttpStatusCode.Created);

        Assert.Equal("UnderReview", created.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, created.GetProperty("confirmedAt").ValueKind);
        Assert.Equal(1, await NotificationsAsync(_provider.Id, NotificationType.ServiceRequestSubmitted));
    }

    // ── the conversation ─────────────────────────────────────────────

    [Fact(DisplayName = "Messages: both sides talk, unread counts and read marks work, strangers and admin cannot write")]
    public async Task Messages_BetweenTheTwoSides()
    {
        var id = await InquireAsync();
        var stranger = await _factory.SeedUserAsync("stranger");

        var reply = await ReadJson(await ProviderClient.PostAsJsonAsync($"/api/ServiceRequests/{id}/messages",
            new { body = "السعر 200 دولار والدفع عند التسليم، والمدة 3 أيام." }), HttpStatusCode.Created);
        Assert.Equal("Provider", reply.GetProperty("senderParty").GetString());
        Assert.True(reply.GetProperty("isMine").GetBoolean());

        var thread = await ReadJson(await CustomerClient.GetAsync($"/api/ServiceRequests/{id}/messages"), HttpStatusCode.OK);
        Assert.Equal(2, thread.GetArrayLength());
        Assert.Equal("Requester", thread[0].GetProperty("senderParty").GetString());
        Assert.False(thread[1].GetProperty("isMine").GetBoolean());

        // The customer has one unread message; opening the thread marks it read.
        var before = await ReadJson(await CustomerClient.GetAsync("/api/ServiceRequests/mine"), HttpStatusCode.OK);
        Assert.Equal(1, before.EnumerateArray().Single().GetProperty("unreadMessageCount").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await CustomerClient.PutAsync($"/api/ServiceRequests/{id}/messages/read", null)).StatusCode);
        var after = await ReadJson(await CustomerClient.GetAsync("/api/ServiceRequests/mine"), HttpStatusCode.OK);
        Assert.Equal(0, after.EnumerateArray().Single().GetProperty("unreadMessageCount").GetInt32());

        // The provider's own unread (the customer's first message) is untouched by the customer's read mark.
        var inbox = await ReadJson(await ProviderClient.GetAsync("/api/ServiceRequests/provider-inbox"), HttpStatusCode.OK);
        Assert.Equal(1, inbox.EnumerateArray().Single().GetProperty("unreadMessageCount").GetInt32());

        var strangerClient = _factory.AuthedClient(stranger.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await strangerClient.GetAsync($"/api/ServiceRequests/{id}/messages")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await strangerClient.PostAsJsonAsync($"/api/ServiceRequests/{id}/messages", new { body = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync($"/api/ServiceRequests/{id}/messages")).StatusCode);

        // An admin reads the thread but is not a side of it.
        var adminView = await ReadJson(await AdminClient.GetAsync($"/api/ServiceRequests/{id}/messages"), HttpStatusCode.OK);
        Assert.Equal(2, adminView.GetArrayLength());
        Assert.All(adminView.EnumerateArray(), m => Assert.False(m.GetProperty("isMine").GetBoolean()));
        Assert.Equal(HttpStatusCode.Forbidden, (await AdminClient.PostAsJsonAsync($"/api/ServiceRequests/{id}/messages", new { body = "x" })).StatusCode);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await CustomerClient.PostAsJsonAsync($"/api/ServiceRequests/{id}/messages", new { body = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await CustomerClient.PostAsJsonAsync($"/api/ServiceRequests/{id}/messages", new { body = new string('م', 1001) })).StatusCode);
    }

    [Fact(DisplayName = "Messages: a burst from one person notifies the other side once, and no message body leaks into a notification")]
    public async Task Messages_BurstNotifiesOnce()
    {
        var id = await InquireAsync();

        for (var i = 0; i < 3; i++)
            await ReadJson(await ProviderClient.PostAsJsonAsync($"/api/ServiceRequests/{id}/messages", new { body = $"سر المزوّد {i}" }), HttpStatusCode.Created);

        Assert.Equal(1, await NotificationsAsync(_customer.Id, NotificationType.ServiceRequestMessage));

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var all = await db.Notifications.AsNoTracking().Where(n => n.RecipientId == _customer.Id).ToListAsync();
            Assert.DoesNotContain(all, n => (n.Message ?? "").Contains("سر المزوّد"));
        });
    }

    [Fact(DisplayName = "Messages: a closed request takes no more messages, but its thread stays readable")]
    public async Task Messages_ClosedRequest()
    {
        var id = await InquireAsync();
        await ReadJson(await CustomerClient.PutAsJsonAsync($"/api/ServiceRequests/{id}/cancel", new { requesterReason = "ChangedMind" }), HttpStatusCode.OK);

        Assert.Equal(HttpStatusCode.Conflict, (await CustomerClient.PostAsJsonAsync($"/api/ServiceRequests/{id}/messages", new { body = "hello?" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ProviderClient.PostAsJsonAsync($"/api/ServiceRequests/{id}/messages", new { body = "hello?" })).StatusCode);
        Assert.Equal(1, (await ReadJson(await CustomerClient.GetAsync($"/api/ServiceRequests/{id}/messages"), HttpStatusCode.OK)).GetArrayLength());
    }

    // ── the customer confirms ────────────────────────────────────────

    [Fact(DisplayName = "Confirm: only the customer, only once, and only then can the provider accept")]
    public async Task Confirm_MakesItARequest()
    {
        var id = await InquireAsync("Request");

        // Not a request yet: the provider cannot accept it.
        Assert.Equal(HttpStatusCode.Conflict, (await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{id}/accept", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ProviderClient.PutAsync($"/api/ServiceRequests/{id}/confirm", null)).StatusCode);

        var confirmed = await ReadJson(await CustomerClient.PutAsync($"/api/ServiceRequests/{id}/confirm", null), HttpStatusCode.OK);
        Assert.Equal("UnderReview", confirmed.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, confirmed.GetProperty("confirmedAt").ValueKind);
        Assert.Equal(1, await NotificationsAsync(_provider.Id, NotificationType.ServiceRequestSubmitted));
        Assert.Equal(HttpStatusCode.Conflict, (await CustomerClient.PutAsync($"/api/ServiceRequests/{id}/confirm", null)).StatusCode);

        var accepted = await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{id}/accept", new { providerNote = "تم" }), HttpStatusCode.OK);
        Assert.Equal("Accepted", accepted.GetProperty("status").GetString());

        // The contact appears now, and only for the customer.
        var asCustomer = await ReadJson(await CustomerClient.GetAsync($"/api/ServiceRequests/{id}"), HttpStatusCode.OK);
        Assert.Equal(ProviderPhone, asCustomer.GetProperty("providerContact").GetProperty("phone").GetString());

        var stats = await ReadJson(await AdminClient.GetAsync("/api/admin/service-requests/stats"), HttpStatusCode.OK);
        var totals = stats.GetProperty("totals");
        Assert.Equal(1, totals.GetProperty("inquiries").GetInt32());
        Assert.Equal(1, totals.GetProperty("requested").GetInt32());
        Assert.Equal(1.0, totals.GetProperty("conversionRate").GetDouble());
    }

    [Fact(DisplayName = "Confirm: a stranger cannot, and neither can anyone once the inquiry was declined")]
    public async Task Confirm_Guards()
    {
        var stranger = await _factory.SeedUserAsync("stranger");
        var id = await InquireAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.AuthedClient(stranger.AccessToken).PutAsync($"/api/ServiceRequests/{id}/confirm", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CustomerClient.PutAsync($"/api/ServiceRequests/{Guid.NewGuid()}/confirm", null)).StatusCode);

        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{id}/reject",
            new { reason = "خارج منطقتنا", reasonCode = "OutsideCoverage" }), HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, (await CustomerClient.PutAsync($"/api/ServiceRequests/{id}/confirm", null)).StatusCode);
    }

    [Fact(DisplayName = "Dropped inquiries: declined or withdrawn before confirmation are not cancelled requests in the statistics")]
    public async Task DroppedInquiries_AreNotRequests()
    {
        var declined = await InquireAsync();
        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{declined}/reject",
            new { reason = "لا أستطيع", reasonCode = "ProviderUnavailable" }), HttpStatusCode.OK);
        var withdrawn = await InquireAsync();
        await ReadJson(await CustomerClient.PutAsJsonAsync($"/api/ServiceRequests/{withdrawn}/cancel", new { requesterReason = "PriceTooHigh" }), HttpStatusCode.OK);
        var real = await ConfirmedAsync();
        await ReadJson(await CustomerClient.PutAsJsonAsync($"/api/ServiceRequests/{real}/cancel", new { requesterReason = "ChangedMind" }), HttpStatusCode.OK);

        var totals = (await ReadJson(await AdminClient.GetAsync("/api/admin/service-requests/stats"), HttpStatusCode.OK)).GetProperty("totals");

        Assert.Equal(3, totals.GetProperty("inquiries").GetInt32());
        Assert.Equal(1, totals.GetProperty("requested").GetInt32());
        Assert.Equal(1, totals.GetProperty("cancelled").GetInt32());
        Assert.Equal(0, totals.GetProperty("rejected").GetInt32());
        Assert.Equal(2, totals.GetProperty("inquiriesDropped").GetInt32());
        Assert.Equal(1.0 / 3, totals.GetProperty("conversionRate").GetDouble(), 3);
    }

    // ── the clock ────────────────────────────────────────────────────

    [Fact(DisplayName = "Reminder: an unconfirmed inquiry is reminded once after two days, a confirmed or younger one never")]
    public async Task Reminder_OnceForUnconfirmedInquiry()
    {
        var old = await InquireAsync();
        var confirmed = await ConfirmedAsync();
        var now = DateTime.UtcNow;

        await SweepAsync(now.AddHours(40));
        Assert.Equal(0, await NotificationsAsync(_customer.Id, NotificationType.ServiceRequestConfirmReminder));

        await SweepAsync(now.AddHours(49));
        Assert.Equal(1, await NotificationsAsync(_customer.Id, NotificationType.ServiceRequestConfirmReminder));

        // Idempotent: a later sweep does not remind again.
        await SweepAsync(now.AddHours(80));
        Assert.Equal(1, await NotificationsAsync(_customer.Id, NotificationType.ServiceRequestConfirmReminder));

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.NotNull((await db.ServiceRequests.SingleAsync(r => r.Id == old)).ReminderSentAt);
            Assert.Null((await db.ServiceRequests.SingleAsync(r => r.Id == confirmed)).ReminderSentAt);
        });
    }

    [Fact(DisplayName = "Expiry: a month without contact closes the request as Expired, keeps everything, and says who was silent")]
    public async Task Expiry_AfterAMonthWithoutContact()
    {
        var providerSilent = await InquireAsync();              // the customer wrote, the provider never answered
        var customerSilent = await ConfirmedAsync();
        await ReadJson(await ProviderClient.PostAsJsonAsync($"/api/ServiceRequests/{customerSilent}/messages", new { body = "أرسل لي الصور" }), HttpStatusCode.Created);
        var inProgress = await ConfirmedAsync();
        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{inProgress}/accept", new { }), HttpStatusCode.OK);
        await ReadJson(await ProviderClient.PutAsJsonAsync($"/api/ServiceRequests/{inProgress}/schedule", new { scheduledAt = DateTime.UtcNow.AddDays(1) }), HttpStatusCode.OK);
        await ReadJson(await ProviderClient.PutAsync($"/api/ServiceRequests/{inProgress}/start", null), HttpStatusCode.OK);

        var now = DateTime.UtcNow;
        await SweepAsync(now.AddDays(29));
        Assert.Equal(0, await NotificationsAsync(_customer.Id, NotificationType.ServiceRequestExpired));

        await SweepAsync(now.AddDays(31));

        var detail = await ReadJson(await AdminClient.GetAsync($"/api/admin/service-requests/{providerSilent}"), HttpStatusCode.OK);
        Assert.Equal("Expired", detail.GetProperty("request").GetProperty("status").GetString());
        Assert.Equal("Provider", detail.GetProperty("request").GetProperty("expiredWaitingOn").GetString());
        Assert.Equal(1, detail.GetProperty("messages").GetArrayLength());           // the conversation is kept
        Assert.Contains(detail.GetProperty("history").EnumerateArray(), h => h.GetProperty("toStatus").GetString() == "Expired");

        var second = await ReadJson(await AdminClient.GetAsync($"/api/admin/service-requests/{customerSilent}"), HttpStatusCode.OK);
        Assert.Equal("Expired", second.GetProperty("request").GetProperty("status").GetString());
        Assert.Equal("Requester", second.GetProperty("request").GetProperty("expiredWaitingOn").GetString());

        // In progress is never expired by the clock: the provider is doing the work.
        var running = await ReadJson(await AdminClient.GetAsync($"/api/admin/service-requests/{inProgress}"), HttpStatusCode.OK);
        Assert.Equal("InProgress", running.GetProperty("request").GetProperty("status").GetString());

        // Both sides are told, once, and a repeated sweep changes nothing.
        Assert.Equal(2, await NotificationsAsync(_customer.Id, NotificationType.ServiceRequestExpired));
        Assert.Equal(2, await NotificationsAsync(_provider.Id, NotificationType.ServiceRequestExpired));
        await SweepAsync(now.AddDays(40));
        Assert.Equal(2, await NotificationsAsync(_customer.Id, NotificationType.ServiceRequestExpired));

        // It is final: no messages, no cancel, no confirm.
        Assert.Equal(HttpStatusCode.Conflict, (await CustomerClient.PostAsJsonAsync($"/api/ServiceRequests/{providerSilent}/messages", new { body = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CustomerClient.PutAsJsonAsync($"/api/ServiceRequests/{providerSilent}/cancel", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CustomerClient.PutAsync($"/api/ServiceRequests/{providerSilent}/confirm", null)).StatusCode);

        // Statistics: an expired inquiry is "dropped", an expired confirmed request is "expired", and the silent party is counted.
        var stats = await ReadJson(await AdminClient.GetAsync("/api/admin/service-requests/stats"), HttpStatusCode.OK);
        var totals = stats.GetProperty("totals");
        Assert.Equal(1, totals.GetProperty("inquiriesDropped").GetInt32());
        Assert.Equal(1, totals.GetProperty("expired").GetInt32());
        var parties = stats.GetProperty("expiryReasons").EnumerateArray().ToDictionary(r => r.GetProperty("reason").GetString()!, r => r.GetProperty("count").GetInt32());
        Assert.Equal(1, parties["Provider"]);
        Assert.Equal(1, parties["Requester"]);
    }

    [Fact(DisplayName = "Expiry: a message inside the month keeps the request alive, and an expired one frees the slot for a new request")]
    public async Task Expiry_ActivityKeepsItAlive_AndFreesTheSlot()
    {
        var property = await NewPropertyAsync();
        var created = await ReadJson(await CustomerClient.PostAsJsonAsync("/api/ServiceRequests",
            new { propertyId = property, serviceOfferingId = _offering, requesterNote = "hi", intent = "Inquire" }), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();

        // 20 days in, a message arrives: its month starts again.
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            await db.ServiceRequests.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.LastActivityAt, DateTime.UtcNow.AddDays(-20)));
        });
        await ReadJson(await ProviderClient.PostAsJsonAsync($"/api/ServiceRequests/{id}/messages", new { body = "جاهز" }), HttpStatusCode.Created);

        await SweepAsync(DateTime.UtcNow.AddDays(15));
        Assert.Equal("Inquiry", (await ReadJson(await CustomerClient.GetAsync($"/api/ServiceRequests/{id}"), HttpStatusCode.OK)).GetProperty("status").GetString());

        await SweepAsync(DateTime.UtcNow.AddDays(31));
        Assert.Equal("Expired", (await ReadJson(await CustomerClient.GetAsync($"/api/ServiceRequests/{id}"), HttpStatusCode.OK)).GetProperty("status").GetString());

        // The same property and service can be asked for again.
        await ReadJson(await CustomerClient.PostAsJsonAsync("/api/ServiceRequests",
            new { propertyId = property, serviceOfferingId = _offering, requesterNote = "again", intent = "Request" }), HttpStatusCode.Created);
    }

    // ── admin ────────────────────────────────────────────────────────

    [Fact(DisplayName = "Admin: the list separates inquiries from requests and shows the thread length")]
    public async Task Admin_ListAndDetail()
    {
        var inquiry = await InquireAsync();
        await ReadJson(await ProviderClient.PostAsJsonAsync($"/api/ServiceRequests/{inquiry}/messages", new { body = "أهلًا" }), HttpStatusCode.Created);
        var request = await ConfirmedAsync();

        var inquiries = await ReadJson(await AdminClient.GetAsync("/api/admin/service-requests?confirmed=false"), HttpStatusCode.OK);
        Assert.Equal(1, inquiries.GetProperty("total").GetInt32());
        var row = inquiries.GetProperty("items")[0];
        Assert.Equal(inquiry, row.GetProperty("id").GetGuid());
        Assert.Equal(2, row.GetProperty("messageCount").GetInt32());
        Assert.Equal("Inquire", row.GetProperty("intent").GetString());

        var requests = await ReadJson(await AdminClient.GetAsync("/api/admin/service-requests?confirmed=true"), HttpStatusCode.OK);
        Assert.Equal(request, requests.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid());

        var byStatus = await ReadJson(await AdminClient.GetAsync("/api/admin/service-requests?status=Inquiry"), HttpStatusCode.OK);
        Assert.Equal(1, byStatus.GetProperty("total").GetInt32());

        var detail = await ReadJson(await AdminClient.GetAsync($"/api/admin/service-requests/{inquiry}"), HttpStatusCode.OK);
        Assert.Equal(2, detail.GetProperty("messages").GetArrayLength());
        Assert.Equal("أهلًا", detail.GetProperty("messages")[1].GetProperty("body").GetString());
    }
}
