using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Integration.Tests.Valuation;

/// <summary>
/// Stage 10 — the Valuation module's real-HTTP security matrix, covering exactly the six
/// rejection scenarios this stage's own spec enumerates by name. Every assertion here hits the
/// actual running host (real [Authorize]/[Authorize(Roles=...)] pipeline, real JwtBearer
/// validation, real Postgres) — never a mocked ISender — same rigor
/// InvestmentAuthorizationMatrixTests already established for the Investments module. Each
/// negative case is paired with a positive control proving the endpoint isn't simply rejecting
/// everything.
/// </summary>
[Trait("Feature", "Valuation")]
[Trait("Category", "Authorization")]
public sealed class ValuationSecurityMatrixTests : IClassFixture<ValuationApiTestFactory>, IAsyncLifetime
{
    private readonly ValuationApiTestFactory _factory;

    public ValuationSecurityMatrixTests(ValuationApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ── 1. Agency A → Agency B's Inquiry/Invitation ────────────────

    [Fact(DisplayName = "Agency A cannot submit a response to Agency B's invitation (403)")]
    public async Task AgencyA_RespondingToAgencyBsInvitation_Returns403()
    {
        var governorateId = await _factory.SeedGovernorateAsync();
        var ownerA = await _factory.SeedUserAsync("agency-a-owner", RoleNames.AgencyOwner);
        var ownerB = await _factory.SeedUserAsync("agency-b-owner", RoleNames.AgencyOwner);
        await _factory.SeedAgencyWithOwnerAsync(ownerA.Id);
        var agencyBId = await _factory.SeedAgencyWithOwnerAsync(ownerB.Id);

        var inquiryId = await _factory.SeedInquiryAsync(governorateId);
        var invitationForB = await _factory.SeedSentInvitationAsync(inquiryId, agencyBId, DateTime.UtcNow);

        var response = await _factory.AuthedClient(ownerA.AccessToken).PostAsJsonAsync(
            $"/api/ValuationOfficeInvitations/{invitationForB}/respond",
            new { EstimatedPrice = 100_000m, Notes = (string?)null });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Positive control: Agency B can submit a response to its own invitation (200)")]
    public async Task AgencyB_RespondingToItsOwnInvitation_Returns200()
    {
        var governorateId = await _factory.SeedGovernorateAsync();
        var ownerB = await _factory.SeedUserAsync("agency-b-owner", RoleNames.AgencyOwner);
        var agencyBId = await _factory.SeedAgencyWithOwnerAsync(ownerB.Id);

        var inquiryId = await _factory.SeedInquiryAsync(governorateId);
        var invitationId = await _factory.SeedSentInvitationAsync(inquiryId, agencyBId, DateTime.UtcNow);

        var response = await _factory.AuthedClient(ownerB.AccessToken).PostAsJsonAsync(
            $"/api/ValuationOfficeInvitations/{invitationId}/respond",
            new { EstimatedPrice = 100_000m, Notes = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "Agency A's own dashboard never lists Agency B's invitations")]
    public async Task AgencyA_OwnDashboard_NeverListsAgencyBsInvitations()
    {
        var governorateId = await _factory.SeedGovernorateAsync();
        var ownerA = await _factory.SeedUserAsync("agency-a-owner", RoleNames.AgencyOwner);
        var ownerB = await _factory.SeedUserAsync("agency-b-owner", RoleNames.AgencyOwner);
        await _factory.SeedAgencyWithOwnerAsync(ownerA.Id);
        var agencyBId = await _factory.SeedAgencyWithOwnerAsync(ownerB.Id);

        var inquiryId = await _factory.SeedInquiryAsync(governorateId);
        var invitationForB = await _factory.SeedSentInvitationAsync(inquiryId, agencyBId, DateTime.UtcNow);

        var response = await _factory.AuthedClient(ownerA.AccessToken).GetAsync("/api/ValuationOfficeInvitations/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(invitationForB.ToString(), body);
    }

    // ── 2. Customer A → Customer B's Inquiry ───────────────────────

    [Fact(DisplayName = "Customer A cannot read Customer B's inquiry status (403)")]
    public async Task CustomerA_ReadingCustomerBsInquiry_Returns403()
    {
        var governorateId = await _factory.SeedGovernorateAsync();
        var customerA = await _factory.SeedUserAsync("customer-a", RoleNames.User);
        var customerB = await _factory.SeedUserAsync("customer-b", RoleNames.User);

        var customerBsInquiryId = await _factory.SeedInquiryAsync(governorateId, requesterId: customerB.Id);

        var response = await _factory.AuthedClient(customerA.AccessToken)
            .GetAsync($"/api/ValuationInquiries/{customerBsInquiryId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Positive control: Customer B can read their own inquiry status (200)")]
    public async Task CustomerB_ReadingOwnInquiry_Returns200()
    {
        var governorateId = await _factory.SeedGovernorateAsync();
        var customerB = await _factory.SeedUserAsync("customer-b", RoleNames.User);
        var inquiryId = await _factory.SeedInquiryAsync(governorateId, requesterId: customerB.Id);

        var response = await _factory.AuthedClient(customerB.AccessToken)
            .GetAsync($"/api/ValuationInquiries/{inquiryId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── 3. Customer → Agency internal data ─────────────────────────

    [Fact(DisplayName = "A customer's own inquiry status never exposes Agency-internal fields")]
    public async Task CustomerInquiryStatus_NeverExposesAgencyInternalData()
    {
        var governorateId = await _factory.SeedGovernorateAsync();
        var customer = await _factory.SeedUserAsync("customer", RoleNames.User);
        var owner = await _factory.SeedUserAsync("agency-owner", RoleNames.AgencyOwner);
        await _factory.SeedAgencyWithOwnerAsync(owner.Id);

        var inquiryId = await _factory.SeedInquiryAsync(governorateId, requesterId: customer.Id);
        // Reuse the same agency for the invitation via a second seed call — SeedAgencyWithOwnerAsync
        // already returned the id; call it once and reuse would be cleaner, but a fresh office
        // works just as well here since only its estimate needs to be Responded.
        var officeOwner = await _factory.SeedUserAsync("office-owner", RoleNames.AgencyOwner);
        var agencyId = await _factory.SeedAgencyWithOwnerAsync(officeOwner.Id);
        await _factory.SeedRespondedInvitationAsync(inquiryId, agencyId);

        var response = await _factory.AuthedClient(customer.AccessToken)
            .GetAsync($"/api/ValuationInquiries/{inquiryId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        // AgencyName is allowed (already public via /agencies/{slug} — see ValuationEstimateDto's
        // own doc comment); none of the agency's actual internal/back-office fields are.
        Assert.DoesNotContain("contactEmail", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contactPhone", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("licenseNumber", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requiresManualReview", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ownerUserId", body, StringComparison.OrdinalIgnoreCase);
    }

    // ── 4. Agency → Customer contact before consent ────────────────

    [Fact(DisplayName = "An office's own dashboard never carries customer contact info before consent")]
    public async Task OfficeDashboard_NeverExposesCustomerContact_BeforeConsent()
    {
        var governorateId = await _factory.SeedGovernorateAsync();
        var owner = await _factory.SeedUserAsync("agency-owner", RoleNames.AgencyOwner);
        var agencyId = await _factory.SeedAgencyWithOwnerAsync(owner.Id);

        var inquiryId = await _factory.SeedInquiryAsync(governorateId);
        await _factory.SeedRespondedInvitationAsync(inquiryId, agencyId);

        var response = await _factory.AuthedClient(owner.AccessToken).GetAsync("/api/ValuationOfficeInvitations/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("\"customerContactPhone\":null", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"customerContactEmail\":null", body, StringComparison.OrdinalIgnoreCase);
    }

    // ── 5. Expired invitation → submit response ────────────────────

    [Fact(DisplayName = "Submitting a response to an expired invitation is rejected (409), even though its Status still reads Sent")]
    public async Task SubmitResponse_ToAnExpiredInvitation_Returns409()
    {
        var governorateId = await _factory.SeedGovernorateAsync();
        var owner = await _factory.SeedUserAsync("agency-owner", RoleNames.AgencyOwner);
        var agencyId = await _factory.SeedAgencyWithOwnerAsync(owner.Id);

        // The parent inquiry's 24h ExpiresAt is computed from ITS OWN creation time (Stage 2),
        // not from the invitation's SentAt — so an inquiry created 25h ago is already past its
        // deadline regardless of when the invitation itself was sent.
        var inquiryId = await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var inquiry = ValuationInquiry.Create(
                governorateId, ListingType.ForSale, DateTime.UtcNow.AddHours(-25));
            inquiry.MarkAwaitingOfficeResponses(DateTime.UtcNow.AddHours(-25));
            db.ValuationInquiries.Add(inquiry);
            await db.SaveChangesAsync();
            return inquiry.Id;
        });

        var invitationId = await _factory.SeedSentInvitationAsync(inquiryId, agencyId, DateTime.UtcNow.AddHours(-25));

        var response = await _factory.AuthedClient(owner.AccessToken).PostAsJsonAsync(
            $"/api/ValuationOfficeInvitations/{invitationId}/respond",
            new { EstimatedPrice = 100_000m, Notes = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── 6. Unauthorized user → Admin endpoint ──────────────────────

    [Fact(DisplayName = "Anonymous: the Valuation admin dashboard is rejected without a token (401)")]
    public async Task Anonymous_ValuationAdminDashboard_Returns401()
    {
        var response = await _factory.AuthedClient().GetAsync("/api/admin/valuation-inquiries");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "Authenticated non-admin: the Valuation admin dashboard is rejected (403)")]
    public async Task NonAdmin_ValuationAdminDashboard_Returns403()
    {
        var user = await _factory.SeedUserAsync("plain-user", RoleNames.User);
        var response = await _factory.AuthedClient(user.AccessToken).GetAsync("/api/admin/valuation-inquiries");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact(DisplayName = "Positive control: an Admin-role token can reach the Valuation admin dashboard (200)")]
    public async Task Admin_ValuationAdminDashboard_Returns200()
    {
        var admin = await _factory.SeedUserAsync("admin", RoleNames.Admin);
        var response = await _factory.AuthedClient(admin.AccessToken).GetAsync("/api/admin/valuation-inquiries");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
