using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Valuation.Interfaces;
using HudhudNestApi.Domain.Notifications.Enums;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Domain.Valuation.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Integration.Tests.Valuation;

/// <summary>
/// Valuation remediation H1/H2 — real-Postgres proof that SubmitOfficeResponseCommandHandler
/// and ValuationInquiryExpiryHostedService's sweep can no longer corrupt each other's work at
/// the 24h boundary. Cannot be a plain unit test: the whole point is the ACTUAL xmin value
/// Postgres assigns and advances on every UPDATE (see PropertyConcurrencyTests.cs, the
/// pre-existing precedent this file mirrors) — Moq proves the application-level decision logic
/// (see SubmitOfficeResponseCommandHandlerTests/ValuationSlaEnforcementServiceTests), this file
/// proves the database actually enforces it.
///
/// Three scenarios, matching the remediation brief exactly:
///   A. Response arrives first -> persisted, inquiry reaches Completed, visible to the customer.
///   B. Expiry (the real sweep) wins first -> the late response is rejected (409), nothing is
///      persisted for it — never "saved but hidden behind an expired invitation".
///   C. Two different offices respond to two different invitations of the SAME inquiry at
///      nearly the same instant -> no lost update, no duplicate response, exactly one
///      completion, exactly one ValuationResultReady notification.
/// </summary>
[Trait("Feature", "Valuation")]
[Trait("Category", "Concurrency")]
public sealed class ValuationConcurrencyTests : IClassFixture<ValuationApiTestFactory>, IAsyncLifetime
{
    private readonly ValuationApiTestFactory _factory;

    public ValuationConcurrencyTests(ValuationApiTestFactory factory) => _factory = factory;

    public Task InitializeAsync() => _factory.PrepareDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(DisplayName = "Scenario A: a response that beats the 24h deadline is persisted and the inquiry reaches Completed")]
    public async Task ScenarioA_ResponseWins_IsPersisted_AndInquiryCompletes()
    {
        var governorateId = await _factory.SeedGovernorateAsync();
        var customer = await _factory.SeedUserAsync("scenario-a-customer");
        var inquiryId = await _factory.SeedInquiryAsync(governorateId, customer.Id);

        var officeOwner = await _factory.SeedUserAsync("scenario-a-office", RoleNames.AgencyOwner);
        var agencyId = await _factory.SeedAgencyWithOwnerAsync(officeOwner.Id);
        var invitationId = await _factory.SeedSentInvitationAsync(inquiryId, agencyId, DateTime.UtcNow.AddHours(-1));

        var client = _factory.AuthedClient(officeOwner.AccessToken);
        var response = await client.PostAsJsonAsync(
            $"/api/ValuationOfficeInvitations/{invitationId}/respond",
            new { EstimatedPrice = 125000m, Notes = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var savedResponse = await db.ValuationOfficeResponses
                .SingleOrDefaultAsync(r => r.InvitationId == invitationId);
            Assert.NotNull(savedResponse);
            Assert.Equal(125000m, savedResponse!.EstimatedPrice);

            var invitation = await db.ValuationOfficeInvitations.SingleAsync(i => i.Id == invitationId);
            Assert.Equal(ValuationOfficeInvitationStatus.Responded, invitation.Status);

            // Remediation H2: with a single invitation, nothing is left outstanding, so the
            // inquiry reaches Completed immediately -- it does not have to wait for the 24h
            // SLA sweep.
            var inquiry = await db.ValuationInquiries.SingleAsync(i => i.Id == inquiryId);
            Assert.Equal(ValuationInquiryStatus.Completed, inquiry.Status);
        });
    }

    [Fact(DisplayName = "Scenario B: once the real sweep expires the invitation, a late response is rejected (409) and nothing is persisted for it")]
    public async Task ScenarioB_ExpiryWins_LateResponseIsRejected_NothingPersisted()
    {
        var governorateId = await _factory.SeedGovernorateAsync();
        var customer = await _factory.SeedUserAsync("scenario-b-customer");

        // Seed the inquiry already 25h old (past its 24h ExpiresAt) and Sent, exactly the
        // "expiry sweep is due to run" state.
        var inquiryId = await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var inquiry = HudhudNestApi.Domain.Valuation.Entities.ValuationInquiry.Create(
                governorateId, HudhudNestApi.Domain.Enums.ListingType.ForSale,
                DateTime.UtcNow.AddHours(-25), requesterId: customer.Id);
            inquiry.MarkAwaitingOfficeResponses(DateTime.UtcNow.AddHours(-25));
            db.ValuationInquiries.Add(inquiry);
            await db.SaveChangesAsync();
            return inquiry.Id;
        });

        var officeOwner = await _factory.SeedUserAsync("scenario-b-office", RoleNames.AgencyOwner);
        var agencyId = await _factory.SeedAgencyWithOwnerAsync(officeOwner.Id);
        var invitationId = await _factory.SeedSentInvitationAsync(inquiryId, agencyId, DateTime.UtcNow.AddHours(-25));

        // Expiry wins the race for real: run the actual sweep service (the same one
        // ValuationInquiryExpiryHostedService calls every 15 minutes) before the office ever
        // gets to respond.
        await _factory.InScopeAsync(async services =>
        {
            var sweep = services.GetRequiredService<IValuationSlaEnforcementService>();
            var result = await sweep.RunSweepAsync(DateTime.UtcNow, batchSize: 50, CancellationToken.None);
            Assert.Equal(1, result.InquiriesExpired);
            Assert.Equal(1, result.InvitationsExpired);
        });

        var client = _factory.AuthedClient(officeOwner.AccessToken);
        var response = await client.PostAsJsonAsync(
            $"/api/ValuationOfficeInvitations/{invitationId}/respond",
            new { EstimatedPrice = 125000m, Notes = (string?)null });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            // The critical assertion: no ValuationOfficeResponse row exists for the rejected
            // invitation. A "saved but hidden behind an expired invitation" outcome would leave
            // a row here despite the 409 -- this proves that cannot happen.
            var savedResponse = await db.ValuationOfficeResponses
                .SingleOrDefaultAsync(r => r.InvitationId == invitationId);
            Assert.Null(savedResponse);

            var invitation = await db.ValuationOfficeInvitations.SingleAsync(i => i.Id == invitationId);
            Assert.Equal(ValuationOfficeInvitationStatus.Expired, invitation.Status);
        });
    }

    [Fact(DisplayName = "Scenario C: two offices responding to two different invitations of the same inquiry at nearly the same instant -- no lost update, exactly one completion, exactly one notification")]
    public async Task ScenarioC_TwoOfficesRespondConcurrently_NoLostUpdate_ExactlyOneCompletion_ExactlyOneNotification()
    {
        var governorateId = await _factory.SeedGovernorateAsync();
        var customer = await _factory.SeedUserAsync("scenario-c-customer");
        var inquiryId = await _factory.SeedInquiryAsync(governorateId, customer.Id);

        var officeOwnerA = await _factory.SeedUserAsync("scenario-c-office-a", RoleNames.AgencyOwner);
        var agencyAId = await _factory.SeedAgencyWithOwnerAsync(officeOwnerA.Id);
        var invitationAId = await _factory.SeedSentInvitationAsync(inquiryId, agencyAId, DateTime.UtcNow.AddHours(-1));

        var officeOwnerB = await _factory.SeedUserAsync("scenario-c-office-b", RoleNames.AgencyOwner);
        var agencyBId = await _factory.SeedAgencyWithOwnerAsync(officeOwnerB.Id);
        var invitationBId = await _factory.SeedSentInvitationAsync(inquiryId, agencyBId, DateTime.UtcNow.AddHours(-1));

        var clientA = _factory.AuthedClient(officeOwnerA.AccessToken);
        var clientB = _factory.AuthedClient(officeOwnerB.AccessToken);

        // Fired together (not awaited one at a time) so both requests' reads and writes
        // genuinely overlap in time -- this is the actual race the H1/H2 remediation targets:
        // both handlers independently re-check "is anything still outstanding for this
        // inquiry" and BOTH could see "no" if they interleave just right, both attempting to
        // complete the same ValuationInquiry row.
        var taskA = clientA.PostAsJsonAsync(
            $"/api/ValuationOfficeInvitations/{invitationAId}/respond",
            new { EstimatedPrice = 100000m, Notes = (string?)null });
        var taskB = clientB.PostAsJsonAsync(
            $"/api/ValuationOfficeInvitations/{invitationBId}/respond",
            new { EstimatedPrice = 110000m, Notes = (string?)null });

        await Task.WhenAll(taskA, taskB);

        // Both responses are legitimate and independent (different invitations, different
        // rows) -- neither must be sacrificed because they happened to race on completing the
        // shared inquiry.
        Assert.Equal(HttpStatusCode.OK, (await taskA).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await taskB).StatusCode);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();

            var responseCount = await db.ValuationOfficeResponses
                .CountAsync(r => r.InvitationId == invitationAId || r.InvitationId == invitationBId);
            Assert.Equal(2, responseCount);

            var invitationA = await db.ValuationOfficeInvitations.SingleAsync(i => i.Id == invitationAId);
            var invitationB = await db.ValuationOfficeInvitations.SingleAsync(i => i.Id == invitationBId);
            Assert.Equal(ValuationOfficeInvitationStatus.Responded, invitationA.Status);
            Assert.Equal(ValuationOfficeInvitationStatus.Responded, invitationB.Status);

            var inquiry = await db.ValuationInquiries.SingleAsync(i => i.Id == inquiryId);
            Assert.Equal(ValuationInquiryStatus.Completed, inquiry.Status);

            // Exactly one ValuationResultReady notification -- whichever of the two requests
            // won the completion race sends it; TrySaveChangesAsync makes the loser a plain
            // no-op rather than a duplicate.
            var resultReadyCount = await db.Notifications.CountAsync(n =>
                n.RecipientId == customer.Id &&
                n.Type == NotificationType.ValuationResultReady &&
                n.RelatedEntityId == inquiryId);
            Assert.Equal(1, resultReadyCount);
        });
    }

    [Fact(DisplayName = "Scenario D (M1): two truly concurrent submissions to the SAME invitation never produce a 500 -- exactly one succeeds, the other is a clean 409, and exactly one response row is ever persisted")]
    public async Task ScenarioD_ConcurrentDuplicateSubmissionsToSameInvitation_NeverSurfaceAsServerError()
    {
        var governorateId = await _factory.SeedGovernorateAsync();
        var customer = await _factory.SeedUserAsync("scenario-d-customer");
        var inquiryId = await _factory.SeedInquiryAsync(governorateId, customer.Id);

        var officeOwner = await _factory.SeedUserAsync("scenario-d-office", RoleNames.AgencyOwner);
        var agencyId = await _factory.SeedAgencyWithOwnerAsync(officeOwner.Id);
        var invitationId = await _factory.SeedSentInvitationAsync(inquiryId, agencyId, DateTime.UtcNow.AddHours(-1));

        // Two independent HttpClients (same agency, same invitation) racing the SAME row.
        // Whichever wins the in-memory Sent->Responded domain guard is only half the story: if
        // both somehow read Sent before either commits, IX_ValuationOfficeResponses_InvitationId
        // is the database-level backstop -- Remediation M1 is specifically that this backstop
        // must surface as 409, never as an unhandled 500.
        var clientPrimary = _factory.AuthedClient(officeOwner.AccessToken);
        var clientSecondary = _factory.AuthedClient(officeOwner.AccessToken);

        var task1 = clientPrimary.PostAsJsonAsync(
            $"/api/ValuationOfficeInvitations/{invitationId}/respond",
            new { EstimatedPrice = 100000m, Notes = (string?)null });
        var task2 = clientSecondary.PostAsJsonAsync(
            $"/api/ValuationOfficeInvitations/{invitationId}/respond",
            new { EstimatedPrice = 100000m, Notes = (string?)null });

        await Task.WhenAll(task1, task2);
        var statusCodes = new[] { (await task1).StatusCode, (await task2).StatusCode };

        Assert.DoesNotContain(HttpStatusCode.InternalServerError, statusCodes);
        Assert.Contains(HttpStatusCode.OK, statusCodes);
        Assert.Single(statusCodes, HttpStatusCode.OK);
        Assert.Contains(HttpStatusCode.Conflict, statusCodes);

        await _factory.InScopeAsync(async services =>
        {
            var db = services.GetRequiredService<AppDbContext>();
            var responseCount = await db.ValuationOfficeResponses.CountAsync(r => r.InvitationId == invitationId);
            Assert.Equal(1, responseCount);
        });
    }
}
