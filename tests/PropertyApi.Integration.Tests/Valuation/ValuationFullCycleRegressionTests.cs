using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Notifications.Enums;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Integration.Tests.Valuation;

/// <summary>
/// Stage 11 — Full Regression. The one comprehensive, real-HTTP test of the whole cycle this
/// stage's own spec draws as a diagram: Customer submits -&gt; Existing listings searched -&gt;
/// Fast Path OR Office Path -&gt; Office Matching -&gt; Invitation -&gt; Office Response -&gt;
/// 24h Expiry if necessary -&gt; Final Result -&gt; Customer sees result -&gt; Admin sees statistics.
/// Exercises every stage's real code through the real Program host, real Postgres, and real
/// JwtBearer tokens (ValuationApiTestFactory, Stage 10's own fixture) — not a single mock.
///
/// Two honest gaps against the spec's own diagram, found while building this test and NOT
/// fixed here (a regression-testing stage does not get to redesign earlier stages' scope — see
/// this module's own rule 9 and the "stop at conflict" protocol):
///   1. No "Reminder" feature exists anywhere in this module. Every earlier stage's own text
///      only ever specified 24h-expiry notifications (Stage 5) — a pre-expiry reminder was
///      never a concrete deliverable of any stage, so this test does not exercise one.
///   2. SubmitOfficeResponseCommandHandler never calls ValuationInquiry.MarkCompleted() — an
///      Office Path inquiry stays AwaitingOfficeResponses even after every invited office has
///      responded, and only ever reaches a terminal state via the 24h SLA sweep (which always
///      lands on Expired, never Completed, for this path). This test pins that as the CURRENT,
///      real behavior rather than the diagram's implied "Office Response -> Final Result"
///      shortcut — see FullCycle_OfficePath_RespondedInvitation_LeavesInquiryAwaiting_NotCompleted
///      below, and this module's own completion report for the recommended follow-up.
/// </summary>
[Trait("Feature", "Valuation")]
[Trait("Category", "Regression")]
public sealed class ValuationFullCycleRegressionTests : IClassFixture<ValuationApiTestFactory>, IAsyncLifetime
{
    private readonly ValuationApiTestFactory _factory;

    public ValuationFullCycleRegressionTests(ValuationApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(DisplayName =
        "Full cycle (Office Path): submission -> matching -> invitations -> one office responds -> " +
        "customer sees the estimate -> consent -> office sees contact -> admin sees it in both views")]
    public async Task FullCycle_OfficePath_FromSubmissionThroughAdminStatistics()
    {
        var governorateId = await _factory.SeedGovernorateAsync();

        var customer = await _factory.SeedUserAsync("customer", RoleNames.User);
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);

        var office1Owner = await _factory.SeedUserAsync("office-1-owner", RoleNames.AgencyOwner);
        var office2Owner = await _factory.SeedUserAsync("office-2-owner", RoleNames.AgencyOwner);
        var office3Owner = await _factory.SeedUserAsync("office-3-owner", RoleNames.AgencyOwner);

        var agency1Id = await _factory.SeedAgencyWithOwnerAsync(office1Owner.Id);
        var agency2Id = await _factory.SeedAgencyWithOwnerAsync(office2Owner.Id);
        var agency3Id = await _factory.SeedAgencyWithOwnerAsync(office3Owner.Id);

        // Office matching (Stage 4) only ever finds agencies whose own GovernorateId matches —
        // SeedAgencyWithOwnerAsync leaves it null, so it's set directly here (a plain public
        // setter, per Agency's own doc comment on this exact field).
        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            foreach (var id in new[] { agency1Id, agency2Id, agency3Id })
            {
                var agency = await db.Agencies.FirstAsync(a => a.Id == id);
                agency.GovernorateId = governorateId;
            }
            await db.SaveChangesAsync();
        });

        // ── Customer submits (no Property listings exist at all -> zero comparables ->
        // Office Path is guaranteed, exactly as GetComparableListingsQueryHandler documents:
        // "no comparables" rather than a Fast Path). ──
        var customerClient = _factory.AuthedClient(customer.AccessToken);

        var createResponse = await customerClient.PostAsJsonAsync("/api/ValuationInquiries", new
        {
            GovernorateId = governorateId,
            DistrictId = (int?)null,
            NeighborhoodId = (int?)null,
            PropertyTypeId = (int?)null,
            Area = (decimal?)null,
            Rooms = (int?)null,
            RequestType = "ForSale",
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await createResponse.Content.ReadFromJsonAsync<CreateValuationInquiryResponse>();
        Assert.NotNull(created);
        Assert.True(created!.RequiresOfficeValuation);
        Assert.Equal(3, created.OfficeMatchCount);
        Assert.True(created.HasMinimumOfficeCoverage);
        Assert.False(created.FastPath.HasComparableListings);

        var inquiryId = created.InquiryId;

        // ── Customer checks status: still awaiting, no estimates yet. ──
        var statusBeforeResponse = await GetStatusAsync(customerClient, inquiryId);
        Assert.Equal("AwaitingOfficeResponses", statusBeforeResponse.Status);
        Assert.Empty(statusBeforeResponse.Estimates);

        // ── Office 1 finds its invitation on its own dashboard and responds. ──
        var office1Client = _factory.AuthedClient(office1Owner.AccessToken);
        var office1Dashboard = await GetMineAsync(office1Client);
        var office1Invitation = Assert.Single(office1Dashboard, i => i.InquiryId == inquiryId);
        Assert.True(office1Invitation.CanRespond);

        var respondResponse = await office1Client.PostAsJsonAsync(
            $"/api/ValuationOfficeInvitations/{office1Invitation.InvitationId}/respond",
            new { EstimatedPrice = 185_000m, Notes = "بناءً على مقارنات مماثلة في المنطقة" });
        Assert.Equal(HttpStatusCode.OK, respondResponse.StatusCode);

        // ── Customer sees the one estimate that came in, with the office's (public) identity
        // but no contact-consent yet. ──
        var statusAfterOneResponse = await GetStatusAsync(customerClient, inquiryId);
        var estimate = Assert.Single(statusAfterOneResponse.Estimates);
        Assert.Equal(agency1Id, estimate.AgencyId);
        Assert.Equal(185_000m, estimate.EstimatedPrice);
        Assert.False(estimate.ContactConsentGiven);

        // ── Customer explicitly consents to share contact info with office 1. ──
        var consentResponse = await customerClient.PostAsJsonAsync(
            $"/api/ValuationInquiries/{inquiryId}/consent",
            new { InvitationId = estimate.InvitationId, ContactPhone = "0991112233", ContactEmail = (string?)null });
        Assert.Equal(HttpStatusCode.OK, consentResponse.StatusCode);

        // ── Office 1's own dashboard now carries exactly the contact info consented to; offices
        // 2 and 3 (never responded to, never consented to) carry none. ──
        office1Dashboard = await GetMineAsync(office1Client);
        var office1Row = Assert.Single(office1Dashboard, i => i.InquiryId == inquiryId);
        Assert.Equal("0991112233", office1Row.CustomerContactPhone);

        var office2Dashboard = await GetMineAsync(_factory.AuthedClient(office2Owner.AccessToken));
        var office2Row = Assert.Single(office2Dashboard, i => i.InquiryId == inquiryId);
        Assert.Null(office2Row.CustomerContactPhone);
        Assert.Null(office2Row.CustomerContactEmail);
        Assert.Equal("Sent", office2Row.InvitationStatus); // never responded to

        // ── Admin sees this inquiry in the dashboard list and correct per-office statistics. ──
        var adminClient = _factory.AuthedClient(admin.AccessToken);

        var adminInquiries = await adminClient.GetFromJsonAsync<AdminInquiriesPage>(
            $"/api/admin/valuation-inquiries?status=AwaitingOfficeResponses&pageSize=50");
        Assert.NotNull(adminInquiries);
        Assert.Contains(adminInquiries!.Data, i => i.Id == inquiryId);

        var stats = await adminClient.GetFromJsonAsync<List<AdminOfficeStatRow>>("/api/admin/valuation-offices/statistics");
        Assert.NotNull(stats);
        var office1Stats = Assert.Single(stats!, s => s.AgencyId == agency1Id);
        Assert.Equal(1, office1Stats.TotalInvitations);
        Assert.Equal(1, office1Stats.TotalResponses);
        Assert.Equal(1m, office1Stats.ResponseRate);

        var office2Stats = Assert.Single(stats!, s => s.AgencyId == agency2Id);
        Assert.Equal(1, office2Stats.TotalInvitations);
        Assert.Equal(0, office2Stats.TotalResponses);
        Assert.Equal(0m, office2Stats.ResponseRate);

        // ── The documented gap: even with a response already in, the inquiry itself has not
        // moved to Completed — see this class's own doc comment. Pinned here as current,
        // real, tested behavior. ──
        var finalStatus = await GetStatusAsync(customerClient, inquiryId);
        Assert.Equal("AwaitingOfficeResponses", finalStatus.Status);
    }

    [Fact(DisplayName =
        "Full cycle (24h SLA path): an unanswered inquiry/invitation is expired by the real sweep " +
        "service, the requester is notified, and both the customer's and admin's views reflect it")]
    public async Task FullCycle_UnansweredInquiry_ExpiresViaRealSlaSweep_NotifiesRequester_VisibleToCustomerAndAdmin()
    {
        var governorateId = await _factory.SeedGovernorateAsync();
        var customer = await _factory.SeedUserAsync("customer", RoleNames.User);
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var officeOwner = await _factory.SeedUserAsync("office-owner", RoleNames.AgencyOwner);
        var agencyId = await _factory.SeedAgencyWithOwnerAsync(officeOwner.Id);

        // Seeded already 25h old, directly through the domain factory (mirrors Stage 10's own
        // expired-invitation test) — the 24h clock starts at CREATION, not at the sweep run, so
        // this is the real, produced-by-the-real-code state a genuinely overdue inquiry has.
        Guid inquiryId = default, invitationId = default;
        var createdAt = DateTime.UtcNow.AddHours(-25);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var agency = await db.Agencies.FirstAsync(a => a.Id == agencyId);
            agency.GovernorateId = governorateId;

            var inquiry = ValuationInquiry.Create(governorateId, ListingType.ForRent, createdAt, requesterId: customer.Id);
            inquiry.MarkAwaitingOfficeResponses(createdAt);
            inquiryId = inquiry.Id;

            var invitation = ValuationOfficeInvitation.Create(agencyId, inquiry.Id, ValuationMatchLevel.Governorate, createdAt);
            invitationId = invitation.Id;

            db.ValuationInquiries.Add(inquiry);
            db.ValuationOfficeInvitations.Add(invitation);
            await db.SaveChangesAsync();
        });

        // ── The real sweep (ValuationInquiryExpiryHostedService's own scoped work, invoked
        // directly here exactly like ValuationSlaEnforcementServiceTests already does for
        // itself) closes both rows and notifies the requester. ──
        await _factory.InScopeAsync(async services =>
        {
            var sweep = services.GetRequiredService<IValuationSlaEnforcementService>();
            var result = await sweep.RunSweepAsync(DateTime.UtcNow, batchSize: 50, CancellationToken.None);

            Assert.Equal(1, result.InquiriesExpired);
            Assert.Equal(1, result.InvitationsExpired);
        });

        var (finalInquiryStatus, finalInvitationStatus, notificationExists) = await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var inquiry = await db.ValuationInquiries.FirstAsync(i => i.Id == inquiryId);
            var invitation = await db.ValuationOfficeInvitations.FirstAsync(i => i.Id == invitationId);
            var hasNotification = await db.Notifications.AnyAsync(n =>
                n.RecipientId == customer.Id && n.Type == NotificationType.ValuationInquiryExpired);

            return (inquiry.Status, invitation.Status, hasNotification);
        });

        Assert.Equal(ValuationInquiryStatus.Expired, finalInquiryStatus);
        Assert.Equal(ValuationOfficeInvitationStatus.Expired, finalInvitationStatus);
        Assert.True(notificationExists, "The real NotifyValuationInquiryExpiredAsync path must persist a Notification for the requester.");

        // ── Customer's own status view reflects the real terminal state. ──
        var customerStatus = await GetStatusAsync(_factory.AuthedClient(customer.AccessToken), inquiryId);
        Assert.Equal("Expired", customerStatus.Status);

        // ── Admin's dashboard list finds it under the Expired filter. ──
        var adminInquiries = await _factory.AuthedClient(admin.AccessToken)
            .GetFromJsonAsync<AdminInquiriesPage>("/api/admin/valuation-inquiries?status=Expired&pageSize=50");
        Assert.NotNull(adminInquiries);
        Assert.Contains(adminInquiries!.Data, i => i.Id == inquiryId);
    }

    // ── HTTP helpers (typed against the controllers' real anonymous response shapes) ──

    private static async Task<StatusResponse> GetStatusAsync(HttpClient client, Guid inquiryId)
    {
        var response = await client.GetAsync($"/api/ValuationInquiries/{inquiryId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StatusResponse>())!;
    }

    private static async Task<List<DashboardRow>> GetMineAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/ValuationOfficeInvitations/mine");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<DashboardRow>>())!;
    }

    // Minimal DTO mirrors — only the fields these tests actually assert on; System.Text.Json
    // ignores every other real property on the actual response.
    private sealed record CreateValuationInquiryResponse(
        Guid InquiryId, bool RequiresOfficeValuation, int OfficeMatchCount, bool HasMinimumOfficeCoverage,
        FastPathPart FastPath);
    private sealed record FastPathPart(bool HasComparableListings);
    private sealed record StatusResponse(string Status, List<EstimatePart> Estimates);
    private sealed record EstimatePart(Guid InvitationId, Guid AgencyId, decimal EstimatedPrice, bool ContactConsentGiven);
    private sealed record DashboardRow(
        Guid InvitationId, Guid InquiryId, string InvitationStatus, bool CanRespond,
        string? CustomerContactPhone, string? CustomerContactEmail);
    private sealed record AdminInquiriesPage(List<AdminInquiryRow> Data);
    private sealed record AdminInquiryRow(Guid Id);
    private sealed record AdminOfficeStatRow(Guid AgencyId, int TotalInvitations, int TotalResponses, decimal ResponseRate);
}
