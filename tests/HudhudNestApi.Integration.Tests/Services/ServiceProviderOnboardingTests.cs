using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Services.Enums;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Integration.Tests.Investments;

namespace HudhudNestApi.Integration.Tests.Services;

/// <summary>
/// The provider onboarding lifecycle over real HTTP, real Identity tokens and real Postgres:
/// apply → admin queue → info request → answer → approve (provider profile created and linked,
/// categories and governorates set, audit + notification) → provider publishes an offering →
/// public catalog → a service request on a property → suspend / reactivate / revoke → re-apply.
/// Plus the authorization, validation, idempotency and concurrency guarantees around it.
/// </summary>
public sealed class ServiceProviderOnboardingTests : IAsyncLifetime
{
    private const string Admin = "Admin";
    private readonly InvestmentApiTestFactory _factory = new();
    private int _damascus;
    private int _aleppo;
    private int _homs;

    public async Task InitializeAsync()
    {
        await _factory.PrepareDatabaseAsync();
        (_damascus, _aleppo, _homs) = await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var damascus = Governorate.Create("دمشق", "Damascus", "SY", 1);
            var aleppo = Governorate.Create("حلب", "Aleppo", "SY", 2);
            var homs = Governorate.Create("حمص", "Homs", "SY", 3);
            db.Governorates.AddRange(damascus, aleppo, homs);
            await db.SaveChangesAsync();
            return (damascus.Id, aleppo.Id, homs.Id);
        });
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private object ApplicationBody(
        string[]? categories = null, int[]? governorates = null, bool terms = true, object? extra = null) => new
        {
            applicantType = "Company",
            displayName = "Damascus Verify Office",
            contactName = "Samir",
            contactEmail = "office@example.test",
            contactPhone = "+963 11 555 1234",
            categories = categories ?? ["Verification", "Valuation"],
            governorateIds = governorates ?? [_damascus, _aleppo],
            experience = "Ten years of property document checks in Damascus.",
            companyName = "Damascus Verify LLC",
            licenseNumber = "LIC-123",
            termsAccepted = terms,
        };

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode}: {body}");
        return string.IsNullOrWhiteSpace(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<(SeededUser Applicant, SeededUser Admin, Guid ApplicationId)> SubmitAsync()
    {
        var applicant = await _factory.SeedUserAsync("applicant");
        var admin = await _factory.SeedUserAsync("admin", Admin);
        var created = await ReadJson(
            await _factory.AuthedClient(applicant.AccessToken).PostAsJsonAsync("/api/ServiceProviderApplications", ApplicationBody()),
            HttpStatusCode.Created);
        return (applicant, admin, created.GetProperty("id").GetGuid());
    }

    private Task<HttpResponseMessage> ApproveAsync(SeededUser admin, Guid applicationId, string[]? categories = null, int[]? governorates = null) =>
        _factory.AuthedClient(admin.AccessToken).PostAsJsonAsync(
            $"/api/admin/service-providers/applications/{applicationId}/approve",
            new
            {
                approvedCategories = categories ?? ["Verification"],
                approvedGovernorateIds = governorates ?? [_damascus],
                verificationLevel = "BusinessVerified",
                note = "Licence checked by phone.",
            });

    [Fact(DisplayName = "Full lifecycle: apply, info request, answer, approve, offer, request, suspend, reactivate, revoke, re-apply")]
    public async Task FullLifecycle()
    {
        var applicant = await _factory.SeedUserAsync("applicant");
        var admin = await _factory.SeedUserAsync("admin", Admin);
        var customer = await _factory.SeedUserAsync("customer");
        var applicantClient = _factory.AuthedClient(applicant.AccessToken);
        var adminClient = _factory.AuthedClient(admin.AccessToken);
        var customerClient = _factory.AuthedClient(customer.AccessToken);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var account = await db.UserAccounts.SingleAsync(u => u.Id == applicant.Id);
            account.ChooseInitialLanguage("de", DateTime.UtcNow);
            await db.SaveChangesAsync();
        });

        // 1. Apply.
        var created = await ReadJson(
            await applicantClient.PostAsJsonAsync("/api/ServiceProviderApplications", ApplicationBody()),
            HttpStatusCode.Created);
        var applicationId = created.GetProperty("id").GetGuid();
        Assert.Equal("Pending", created.GetProperty("status").GetString());
        Assert.Matches(@"^SPA-\d{6}$", created.GetProperty("referenceNumber").GetString()!);

        // 2. It is in the admin queue, filterable by status, category and governorate.
        var queue = await ReadJson(await adminClient.GetAsync(
            $"/api/admin/service-providers/applications?status=Pending&category=Valuation&governorateId={_aleppo}"),
            HttpStatusCode.OK);
        Assert.Equal(1, queue.GetProperty("total").GetInt32());
        var none = await ReadJson(await adminClient.GetAsync(
            $"/api/admin/service-providers/applications?category=Cleaning"), HttpStatusCode.OK);
        Assert.Equal(0, none.GetProperty("total").GetInt32());
        var pending = await ReadJson(await adminClient.GetAsync("/api/admin/service-providers/applications/pending-count"), HttpStatusCode.OK);
        Assert.Equal(1, pending.GetProperty("count").GetInt32());

        // 3. Internal note (applicant must never see it) and an info request.
        await ReadJson(await adminClient.PostAsJsonAsync(
            $"/api/admin/service-providers/applications/{applicationId}/notes",
            new { note = "INTERNAL: call the licence office first", visibleToApplicant = false }), HttpStatusCode.OK);
        var asked = await ReadJson(await adminClient.PostAsJsonAsync(
            $"/api/admin/service-providers/applications/{applicationId}/request-info",
            new { message = "Please add your licence number for Aleppo." }), HttpStatusCode.OK);
        Assert.Equal("NeedsInfo", asked.GetProperty("summary").GetProperty("status").GetString());

        var mine = await ReadJson(await applicantClient.GetAsync("/api/ServiceProviderApplications/me"), HttpStatusCode.OK);
        var myApp = mine.EnumerateArray().Single();
        Assert.Equal("NeedsInfo", myApp.GetProperty("status").GetString());
        Assert.Equal("Please add your licence number for Aleppo.", myApp.GetProperty("infoRequest").GetString());
        var applicantTimeline = myApp.GetProperty("timeline").EnumerateArray().ToList();
        Assert.DoesNotContain(applicantTimeline, e => e.GetProperty("isInternal").GetBoolean());
        Assert.DoesNotContain(applicantTimeline, e => (e.GetProperty("note").GetString() ?? "").Contains("INTERNAL"));

        // 4. The applicant answers; back to Pending.
        var answered = await ReadJson(await applicantClient.PutAsJsonAsync(
            $"/api/ServiceProviderApplications/{applicationId}/respond",
            new
            {
                applicantType = "Company",
                displayName = "Damascus Verify Office",
                contactName = "Samir",
                contactEmail = "office@example.test",
                contactPhone = "+963 11 555 1234",
                categories = new[] { "Verification", "Valuation" },
                governorateIds = new[] { _damascus, _aleppo },
                experience = "Ten years of property document checks.",
                companyName = "Damascus Verify LLC",
                licenseNumber = "LIC-123-ALEPPO",
                message = "Added the Aleppo licence number.",
            }), HttpStatusCode.OK);
        Assert.Equal("Pending", answered.GetProperty("status").GetString());

        // 5. Approve Verification in Damascus only.
        var approved = await ReadJson(await ApproveAsync(admin, applicationId), HttpStatusCode.OK);
        Assert.Equal("Approved", approved.GetProperty("summary").GetProperty("status").GetString());
        var providerId = approved.GetProperty("serviceProviderId").GetGuid();

        // A retry (double click, lost response) changes nothing and still answers 200.
        await ReadJson(await ApproveAsync(admin, applicationId), HttpStatusCode.OK);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var provider = await db.ServiceProviders.SingleAsync(p => p.UserId == applicant.Id);
            Assert.Equal(providerId, provider.Id);
            Assert.Equal(ServiceProviderStatus.Active, provider.Status);
            Assert.Equal(["Verification"], provider.ApprovedCategories);
            Assert.Equal([_damascus], provider.CoverageGovernorateIds);
            Assert.Equal(ServiceProviderVerificationLevel.BusinessVerified, provider.VerificationLevel);

            Assert.Equal(1, await db.AuditLogs.CountAsync(a =>
                a.Action == AuditActions.ServiceProviderApplicationApprovedByAdmin && a.UserId == admin.Id));

            var notification = await db.Notifications.SingleAsync(n =>
                n.RecipientId == applicant.Id && n.Type == NotificationType.ServiceProviderApplicationApproved);
            Assert.Contains("zugelassener", notification.Message); // rendered in the applicant's language (de)
            Assert.Equal(applicationId, notification.RelatedEntityId);
        });

        // 6. Provider dashboard: approved category works, an unapproved one is refused.
        var me = await ReadJson(await applicantClient.GetAsync("/api/ServiceProviders/me"), HttpStatusCode.OK);
        Assert.Equal("Active", me.GetProperty("status").GetString());
        var offering = await ReadJson(await applicantClient.PostAsJsonAsync("/api/ServiceOfferings",
            new { category = "Verification", title = "Ownership document check", description = "Deed and registry check", estimatedDurationDays = 3 }),
            HttpStatusCode.Created);
        var offeringId = offering.GetProperty("id").GetGuid();
        var refused = await applicantClient.PostAsJsonAsync("/api/ServiceOfferings",
            new { category = "Valuation", title = "Valuation", estimatedDurationDays = 2 });
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // 7. Public catalog shows it, filtered by coverage.
        var anonymous = _factory.CreateClient();
        var catalog = await ReadJson(await anonymous.GetAsync("/api/ServiceOfferings?category=Verification"), HttpStatusCode.OK);
        Assert.Single(catalog.EnumerateArray());
        var inDamascus = await ReadJson(await anonymous.GetAsync($"/api/ServiceOfferings?category=Verification&governorateId={_damascus}"), HttpStatusCode.OK);
        Assert.Single(inDamascus.EnumerateArray());
        var inHoms = await ReadJson(await anonymous.GetAsync($"/api/ServiceOfferings?category=Verification&governorateId={_homs}"), HttpStatusCode.OK);
        Assert.Empty(inHoms.EnumerateArray());
        var availability = await ReadJson(await anonymous.GetAsync("/api/ServiceOfferings/categories/availability"), HttpStatusCode.OK);
        Assert.Equal(13, availability.GetArrayLength());
        Assert.Equal(1, availability.EnumerateArray().Single(c => c.GetProperty("category").GetString() == "Verification").GetProperty("offeringCount").GetInt32());

        // 8. A customer requests the service on a Damascus property; a Homs property is outside coverage.
        var damascusProperty = await SeedPropertyInAsync(customer.Id, _damascus);
        var homsProperty = await SeedPropertyInAsync(customer.Id, _homs);
        var request = await ReadJson(await customerClient.PostAsJsonAsync("/api/ServiceRequests",
            new { propertyId = damascusProperty, serviceOfferingId = offeringId, requesterNote = "Please check the deed." }),
            HttpStatusCode.Created);
        var requestId = request.GetProperty("id").GetGuid();
        var outside = await customerClient.PostAsJsonAsync("/api/ServiceRequests",
            new { propertyId = homsProperty, serviceOfferingId = offeringId });
        Assert.Equal(HttpStatusCode.Conflict, outside.StatusCode);

        // 9. Suspend: hidden from the catalog, no new requests, cannot accept, existing request kept.
        await ReadJson(await adminClient.PostAsJsonAsync($"/api/admin/service-providers/{providerId}/suspend",
            new { reason = "Customer complaint under review" }), HttpStatusCode.OK);
        Assert.Empty((await ReadJson(await anonymous.GetAsync("/api/ServiceOfferings?category=Verification"), HttpStatusCode.OK)).EnumerateArray());
        var blocked = await customerClient.PostAsJsonAsync("/api/ServiceRequests",
            new { propertyId = damascusProperty, serviceOfferingId = offeringId });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var accept = await applicantClient.PutAsJsonAsync($"/api/ServiceRequests/{requestId}/accept", new { providerNote = "ok" });
        Assert.Equal(HttpStatusCode.Conflict, accept.StatusCode);
        var customerRequests = await ReadJson(await customerClient.GetAsync("/api/ServiceRequests/mine"), HttpStatusCode.OK);
        Assert.Contains(customerRequests.EnumerateArray(), r => r.GetProperty("id").GetGuid() == requestId);

        // 10. Reactivate: back in the catalog and able to accept.
        await ReadJson(await adminClient.PostAsync($"/api/admin/service-providers/{providerId}/reactivate", null), HttpStatusCode.OK);
        Assert.Single((await ReadJson(await anonymous.GetAsync("/api/ServiceOfferings?category=Verification"), HttpStatusCode.OK)).EnumerateArray());
        await ReadJson(await applicantClient.PutAsJsonAsync($"/api/ServiceRequests/{requestId}/accept", new { providerNote = "ok" }), HttpStatusCode.OK);

        // 11. Admin widens the scope; removing a category deactivates its offerings.
        var widened = await ReadJson(await adminClient.PutAsJsonAsync($"/api/admin/service-providers/{providerId}",
            new
            {
                displayName = "Damascus Verify Office",
                bio = (string?)null,
                contactEmail = "office@example.test",
                contactPhone = "+963 11 555 1234",
                approvedCategories = new[] { "Valuation" },
                coverageGovernorateIds = new[] { _damascus, _homs },
            }), HttpStatusCode.OK);
        Assert.False(widened.GetProperty("offerings").EnumerateArray().Single().GetProperty("isActive").GetBoolean());

        // 12. Revoke: hidden, cannot be reactivated, the applicant may re-apply; approval re-links the same profile.
        await ReadJson(await adminClient.PostAsJsonAsync($"/api/admin/service-providers/{providerId}/revoke",
            new { reason = "Licence expired" }), HttpStatusCode.OK);
        var reactivateRevoked = await adminClient.PostAsync($"/api/admin/service-providers/{providerId}/reactivate", null);
        Assert.Equal(HttpStatusCode.Conflict, reactivateRevoked.StatusCode);

        var reapplied = await ReadJson(
            await applicantClient.PostAsJsonAsync("/api/ServiceProviderApplications", ApplicationBody(["Verification"], [_damascus])),
            HttpStatusCode.Created);
        var relinked = await ReadJson(await ApproveAsync(admin, reapplied.GetProperty("id").GetGuid()), HttpStatusCode.OK);
        Assert.Equal(providerId, relinked.GetProperty("serviceProviderId").GetGuid());

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.ServiceProviders.CountAsync(p => p.UserId == applicant.Id));
            Assert.True(await db.ServiceRequests.AnyAsync(r => r.Id == requestId)); // nothing deleted
            var history = await db.ServiceProviderEvents.Where(e => e.ServiceProviderId == providerId).Select(e => e.Type).ToListAsync();
            Assert.Contains(ServiceProviderEventType.ProviderSuspended, history);
            Assert.Contains(ServiceProviderEventType.ProviderReactivated, history);
            Assert.Contains(ServiceProviderEventType.ProviderRevoked, history);
            Assert.Contains(ServiceProviderEventType.ProviderLinked, history);
        });
    }

    [Fact(DisplayName = "Reject with a reason: applicant sees it, the application is closed, approval afterwards is refused")]
    public async Task Reject_ClosesApplication()
    {
        var (applicant, admin, applicationId) = await SubmitAsync();
        var adminClient = _factory.AuthedClient(admin.AccessToken);

        var missingReason = await adminClient.PostAsJsonAsync(
            $"/api/admin/service-providers/applications/{applicationId}/reject", new { reason = "" });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missingReason.StatusCode);

        await ReadJson(await adminClient.PostAsJsonAsync(
            $"/api/admin/service-providers/applications/{applicationId}/reject",
            new { reason = "Coverage area is outside our launch regions." }), HttpStatusCode.OK);

        var mine = await ReadJson(await _factory.AuthedClient(applicant.AccessToken).GetAsync("/api/ServiceProviderApplications/me"), HttpStatusCode.OK);
        var app = mine.EnumerateArray().Single();
        Assert.Equal("Rejected", app.GetProperty("status").GetString());
        Assert.Equal("Coverage area is outside our launch regions.", app.GetProperty("decisionReason").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await ApproveAsync(admin, applicationId)).StatusCode);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.False(await db.ServiceProviders.AnyAsync(p => p.UserId == applicant.Id));
            Assert.True(await db.Notifications.AnyAsync(n =>
                n.RecipientId == applicant.Id && n.Type == NotificationType.ServiceProviderApplicationRejected));
        });
    }

    [Fact(DisplayName = "Admin endpoints: anonymous 401, a normal user 403 on every action")]
    public async Task AdminEndpoints_RequireAdmin()
    {
        var (applicant, _, applicationId) = await SubmitAsync();
        var user = _factory.AuthedClient(applicant.AccessToken);
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/service-providers/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/service-providers/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync($"/api/admin/service-providers/applications/{applicationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync(
            $"/api/admin/service-providers/applications/{applicationId}/approve",
            new { approvedCategories = new[] { "Verification" }, approvedGovernorateIds = new[] { _damascus } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync(
            $"/api/admin/service-providers/applications/{applicationId}/reject", new { reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/admin/service-providers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsJsonAsync(
            $"/api/admin/service-providers/{Guid.NewGuid()}/suspend", new { reason = "x" })).StatusCode);

        // The applicant could not approve themselves.
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.False(await db.ServiceProviders.AnyAsync());
            Assert.Equal(ServiceProviderApplicationStatus.Pending,
                (await db.ServiceProviderApplications.SingleAsync()).Status);
        });
    }

    [Fact(DisplayName = "IDOR: another user cannot read, answer or withdraw someone else's application")]
    public async Task Applicant_CannotTouchForeignApplication()
    {
        var (_, _, applicationId) = await SubmitAsync();
        var stranger = await _factory.SeedUserAsync("stranger");
        var client = _factory.AuthedClient(stranger.AccessToken);

        var mine = await ReadJson(await client.GetAsync("/api/ServiceProviderApplications/me"), HttpStatusCode.OK);
        Assert.Empty(mine.EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/ServiceProviderApplications/{applicationId}/withdraw", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(
            $"/api/ServiceProviderApplications/{applicationId}/respond",
            new
            {
                applicantType = "Individual",
                displayName = "x",
                contactName = "x",
                contactEmail = "x@example.test",
                categories = new[] { "Verification" },
                governorateIds = new[] { _damascus },
                experience = "x",
                message = "x",
            })).StatusCode);
    }

    [Fact(DisplayName = "Mass assignment: status and applicant id in the body are ignored")]
    public async Task Submit_IgnoresServerOwnedFields()
    {
        var applicant = await _factory.SeedUserAsync("applicant");
        var other = await _factory.SeedUserAsync("other");
        var body = new Dictionary<string, object?>
        {
            ["applicantType"] = "Individual",
            ["displayName"] = "Self Approver",
            ["contactName"] = "Self",
            ["contactEmail"] = "self@example.test",
            ["categories"] = new[] { "Cleaning" },
            ["governorateIds"] = new[] { _damascus },
            ["experience"] = "Cleaning",
            ["termsAccepted"] = true,
            ["status"] = "Approved",
            ["applicantUserId"] = other.Id,
            ["serviceProviderId"] = Guid.NewGuid(),
        };

        var created = await ReadJson(await _factory.AuthedClient(applicant.AccessToken)
            .PostAsJsonAsync("/api/ServiceProviderApplications", body), HttpStatusCode.Created);
        Assert.Equal("Pending", created.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("serviceProviderId").ValueKind);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var app = await db.ServiceProviderApplications.SingleAsync();
            Assert.Equal(applicant.Id, app.ApplicantUserId);
        });
    }

    [Fact(DisplayName = "Validation: no categories, unknown governorate, no terms, no contact → 422; a second open application → 409")]
    public async Task Submit_Validation()
    {
        var applicant = await _factory.SeedUserAsync("applicant");
        var client = _factory.AuthedClient(applicant.AccessToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/ServiceProviderApplications", ApplicationBody([], null))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/ServiceProviderApplications", ApplicationBody(null, [999_999]))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/ServiceProviderApplications", ApplicationBody(terms: false))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/ServiceProviderApplications", new
        {
            applicantType = "Individual",
            displayName = "x",
            contactName = "x",
            categories = new[] { "Verification" },
            governorateIds = new[] { _damascus },
            experience = "x",
            termsAccepted = true,
        })).StatusCode);

        await ReadJson(await client.PostAsJsonAsync("/api/ServiceProviderApplications", ApplicationBody()), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/ServiceProviderApplications", ApplicationBody())).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _factory.CreateClient().PostAsJsonAsync("/api/ServiceProviderApplications", ApplicationBody())).StatusCode);
    }

    [Fact(DisplayName = "Approval outside what was requested is refused; approving in parallel creates exactly one provider")]
    public async Task Approve_ScopeAndConcurrency()
    {
        var (applicant, admin, applicationId) = await SubmitAsync();

        Assert.Equal(HttpStatusCode.Conflict, (await ApproveAsync(admin, applicationId, ["Cleaning"])).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ApproveAsync(admin, applicationId, null, [_homs])).StatusCode);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ApproveAsync(admin, applicationId)));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.ServiceProviders.CountAsync(p => p.UserId == applicant.Id));
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == AuditActions.ServiceProviderApplicationApprovedByAdmin));
            Assert.Equal(1, await db.Notifications.CountAsync(n => n.Type == NotificationType.ServiceProviderApplicationApproved));
        });
    }

    [Fact(DisplayName = "A failing notification never undoes or fails an approval")]
    public async Task Approve_NotificationFailure_StillApproves()
    {
        var applicant = await _factory.SeedUserAsync("applicant");
        var admin = await _factory.SeedUserAsync("admin", Admin);

        using var failing = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationService>();
            services.AddScoped<INotificationService, ThrowingNotificationService>();
        }));

        HttpClient Client(string token)
        {
            var c = failing.CreateClient();
            c.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            return c;
        }

        var created = await ReadJson(await Client(applicant.AccessToken)
            .PostAsJsonAsync("/api/ServiceProviderApplications", ApplicationBody()), HttpStatusCode.Created);
        var applicationId = created.GetProperty("id").GetGuid();

        var approved = await ReadJson(await Client(admin.AccessToken).PostAsJsonAsync(
            $"/api/admin/service-providers/applications/{applicationId}/approve",
            new { approvedCategories = new[] { "Verification" }, approvedGovernorateIds = new[] { _damascus } }),
            HttpStatusCode.OK);
        Assert.Equal("Approved", approved.GetProperty("summary").GetProperty("status").GetString());
    }

    private Task<Guid> SeedPropertyInAsync(Guid ownerId, int governorateId) =>
        _factory.InScopeAsync(async services =>
        {
            var id = await _factory.SeedPropertyAsync(ownerId);
            var db = services.GetRequiredService<AppDbContext>();
            await db.Properties.Where(p => p.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.GovernorateId, governorateId)
                .SetProperty(p => p.IsPublished, true));
            return id;
        });

    private sealed class ThrowingNotificationService : INotificationService
    {
        private static Task Boom() => throw new InvalidOperationException("notification store down");

        public Task NotifyNewMessageAsync(Guid recipientId, Guid senderId, string senderName, Guid messageId, Guid propertyId, CancellationToken ct = default) => Boom();
        public Task NotifyListingReportAsync(Guid recipientId, NotificationType type, NotificationText message, Guid reportId, Guid? propertyId, string dedupeKey, CancellationToken ct = default) => Boom();
        public Task NotifyPropertyUpdateAsync(Guid recipientId, Guid propertyId, string propertyTitle, NotificationType type, NotificationText detail, Guid? relatedEntityId = null, CancellationToken ct = default) => Boom();
        public Task NotifySavedSearchMatchAsync(Guid recipientId, Guid propertyId, string propertyTitle, string savedSearchName, CancellationToken ct = default) => Boom();
        public Task NotifyUserRatedAsync(Guid recipientId, Guid raterId, string raterName, double overallScore, CancellationToken ct = default) => Boom();
        public Task NotifyListingExpiringSoonAsync(Guid recipientId, Guid propertyId, string propertyTitle, int daysRemaining, CancellationToken ct = default) => Boom();
        public Task NotifyListingExpiredAsync(Guid recipientId, Guid propertyId, string propertyTitle, int graceDaysRemaining, CancellationToken ct = default) => Boom();
        public Task NotifyAgencyInvitationReceivedAsync(Guid recipientId, Guid invitationId, string agencyName, string inviterName, CancellationToken ct = default) => Boom();
        public Task NotifyAgencyMemberInquiryAsync(Guid recipientId, Guid messageId, Guid propertyId, string propertyTitle, string agencyName, string customerName, string memberName, CancellationToken ct = default) => Boom();
        public Task NotifyAgencyInvitationRespondedAsync(Guid recipientId, Guid invitationId, string targetUserName, bool accepted, CancellationToken ct = default) => Boom();
        public Task NotifyShortStayBookingUpdateAsync(Guid recipientId, Guid bookingId, string listingTitle, NotificationType type, NotificationText detail, CancellationToken ct = default) => Boom();
        public Task NotifyServiceProviderUpdateAsync(Guid recipientId, NotificationType type, NotificationText message, Guid relatedEntityId, CancellationToken ct = default) => Boom();
        public Task<IReadOnlyList<Application.Notifications.DTOs.NotificationDto>> GetUserNotificationsAsync(Guid userId, int page, int pageSize, CancellationToken ct = default) => throw new NotImplementedException();
        public Task MarkAsReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default) => Boom();
        public Task MarkAllAsReadAsync(Guid userId, CancellationToken ct = default) => Boom();
        public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken ct = default) => Task.FromResult(0);
        public Task<bool> DeleteNotificationAsync(Guid notificationId, Guid userId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> SoftDeleteNotificationAsync(Guid notificationId, Guid userId, CancellationToken ct = default) => Task.FromResult(false);
        public Task NotifyValuationInquiryExpiredAsync(Guid recipientId, Guid inquiryId, bool hadPreliminaryEstimate, CancellationToken ct = default) => Boom();
        public Task NotifyValuationOfficeInvitationExpiredAsync(Guid recipientId, Guid invitationId, Guid inquiryId, CancellationToken ct = default) => Boom();
        public Task NotifyValuationResultReadyAsync(Guid recipientId, Guid inquiryId, CancellationToken ct = default) => Boom();
        public Task NotifyValuationInquiryReminderSoonAsync(Guid recipientId, Guid inquiryId, CancellationToken ct = default) => Boom();
    }
}
