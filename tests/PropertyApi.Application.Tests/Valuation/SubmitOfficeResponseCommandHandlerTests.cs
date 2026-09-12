using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.Valuation.Commands.SubmitOfficeResponse;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Tests.Valuation;

/// <summary>
/// Stage 6 — SubmitOfficeResponseCommandHandler's cross-agency authorization and
/// backend-enforced-expiry rules. Uses Moq for every dependency, MockBehavior.Strict, same
/// convention OfficeMatchingServiceTests/ValuationSlaEnforcementServiceTests already
/// established for this module.
///
/// Remediation H2 — also covers the completion rule: an inquiry reaches Completed as soon as
/// every invitation OfficeMatchingService actually sent for it has moved out of Sent (whether
/// that is 1, 2, or 3+ invitations — never a hardcoded "3 responses"), instead of only ever
/// via the 24h SLA sweep.
/// </summary>
public sealed class SubmitOfficeResponseCommandHandlerTests
{
    [Fact]
    public async Task Handle_ValidResponse_MarksInvitationResponded_AndPersistsTheEstimate()
    {
        var (agencyId, actorId, invitation, inquiry) = BuildScenario(invitationExpiresInFuture: true);
        var (handler, invitations, responses, inquiries, agencies, notifications, unitOfWork) = Build();

        SetupHappyPathReads(invitations, inquiries, agencies, invitation, inquiry, actorId, agencyId);
        // The only invitation ever sent for this inquiry is this one — once it is answered,
        // nothing is left outstanding, so this scenario also exercises the H2 completion path.
        invitations.Setup(x => x.GetByInquiryIdAsync(invitation.InquiryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { invitation });
        inquiries.Setup(x => x.Update(inquiry));
        responses.Setup(x => x.AddAsync(It.IsAny<ValuationOfficeResponse>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        unitOfWork.Setup(x => x.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var command = new SubmitOfficeResponseCommand(invitation.Id, actorId, 150000m, "ملاحظة اختبارية");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(invitation.Id, result.InvitationId);
        Assert.Equal(150000m, result.EstimatedPrice);
        Assert.Equal("ملاحظة اختبارية", result.Notes);
        Assert.Equal(ValuationOfficeInvitationStatus.Responded, invitation.Status);
        Assert.Equal(ValuationInquiryStatus.Completed, inquiry.Status);
        invitations.Verify(x => x.Update(invitation), Times.Once);
        inquiries.Verify(x => x.Update(inquiry), Times.Once);
        responses.Verify(x => x.AddAsync(It.Is<ValuationOfficeResponse>(r => r.InvitationId == invitation.Id), It.IsAny<CancellationToken>()), Times.Once);
        // Strict mock on `notifications` has no setup — RequesterId is null (anonymous
        // inquiry, per BuildScenario), so NotifyValuationResultReadyAsync must never be called.
    }

    [Fact]
    public async Task Handle_CallersAgencyDoesNotMatchInvitation_ThrowsForbidden_AndNeverTouchesTheInvitation()
    {
        var (agencyId, actorId, invitation, inquiry) = BuildScenario(invitationExpiresInFuture: true);
        var (handler, invitations, responses, inquiries, agencies, notifications, unitOfWork) = Build();

        var otherAgencyId = Guid.NewGuid();
        var actorAccount = BuildUserAccount(actorId, agencyId: otherAgencyId); // NOT the invited agency

        invitations.Setup(x => x.GetByIdAsync(invitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        agencies.Setup(x => x.GetUserAccountAsync(actorId, It.IsAny<CancellationToken>())).ReturnsAsync(actorAccount);

        var command = new SubmitOfficeResponseCommand(invitation.Id, actorId, 100000m, null);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(command, CancellationToken.None));

        // No Strict-mock setup exists for GetByIdAsync(inquiry)/Update/AddAsync/SaveChangesAsync
        // — reaching any of them would already throw. Status is also asserted directly as
        // belt-and-suspenders confirmation the invitation was never mutated.
        Assert.Equal(ValuationOfficeInvitationStatus.Sent, invitation.Status);
    }

    [Fact]
    public async Task Handle_InvitationPastItsInquirysExpiresAt_ExpiresItAndThrowsConflict_EvenThoughStatusStillReadsSent()
    {
        // The backend-enforcement rule: Status can still read Sent because
        // ValuationInquiryExpiryHostedService's sweep only ticks every 15 minutes — a
        // disabled frontend button is not enforcement. The handler must independently expire
        // the row and refuse the response right now.
        var (agencyId, actorId, invitation, inquiry) = BuildScenario(invitationExpiresInFuture: false);
        var (handler, invitations, responses, inquiries, agencies, notifications, unitOfWork) = Build();

        var actorAccount = BuildUserAccount(actorId, agencyId);

        invitations.Setup(x => x.GetByIdAsync(invitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        agencies.Setup(x => x.GetUserAccountAsync(actorId, It.IsAny<CancellationToken>())).ReturnsAsync(actorAccount);
        inquiries.Setup(x => x.GetByIdAsync(invitation.InquiryId, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);
        invitations.Setup(x => x.Update(invitation));
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var command = new SubmitOfficeResponseCommand(invitation.Id, actorId, 100000m, null);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));

        Assert.Equal(ValuationOfficeInvitationStatus.Expired, invitation.Status);
        invitations.Verify(x => x.Update(invitation), Times.Once);
        // Strict mock on `responses`/`GetByInquiryIdAsync` has no setup -- reaching either
        // would already throw; this is redundant confirmation that no response was ever
        // created, and no completion check was ever attempted, for an expired invitation.
    }

    [Fact]
    public async Task Handle_InvitationAlreadyResponded_ThrowsConflict_NotDuplicated()
    {
        var (agencyId, actorId, invitation, inquiry) = BuildScenario(invitationExpiresInFuture: true);
        invitation.MarkResponded(DateTime.UtcNow.AddMinutes(-5)); // already answered once

        var (handler, invitations, responses, inquiries, agencies, notifications, unitOfWork) = Build();
        var actorAccount = BuildUserAccount(actorId, agencyId);

        invitations.Setup(x => x.GetByIdAsync(invitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        agencies.Setup(x => x.GetUserAccountAsync(actorId, It.IsAny<CancellationToken>())).ReturnsAsync(actorAccount);
        inquiries.Setup(x => x.GetByIdAsync(invitation.InquiryId, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);

        var command = new SubmitOfficeResponseCommand(invitation.Id, actorId, 100000m, null);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
    }

    // ── H2 — completion rule: "no more Sent invitations", not a hardcoded response count ──

    [Fact]
    public async Task Handle_OneOfThreeInvitationsResponded_DoesNotComplete()
    {
        var s = BuildMultiInvitationScenario(totalInvitations: 3, alreadyResponded: 0);

        SetupHappyPathReads(s.Invitations, s.Inquiries, s.Agencies, s.Target, s.Inquiry, s.TargetActorId, s.TargetAgencyId);
        s.Invitations.Setup(x => x.GetByInquiryIdAsync(s.Inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(s.Siblings);
        s.Responses.Setup(x => x.AddAsync(It.IsAny<ValuationOfficeResponse>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        s.UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var command = new SubmitOfficeResponseCommand(s.Target.Id, s.TargetActorId, 100000m, null);
        await s.Handler.Handle(command, CancellationToken.None);

        Assert.Equal(ValuationInquiryStatus.AwaitingOfficeResponses, s.Inquiry.Status);
        // Strict mock: no setup for inquiries.Update -- reaching it would already throw.
    }

    [Fact]
    public async Task Handle_TwoOfThreeInvitationsResponded_DoesNotComplete()
    {
        var s = BuildMultiInvitationScenario(totalInvitations: 3, alreadyResponded: 1);

        SetupHappyPathReads(s.Invitations, s.Inquiries, s.Agencies, s.Target, s.Inquiry, s.TargetActorId, s.TargetAgencyId);
        s.Invitations.Setup(x => x.GetByInquiryIdAsync(s.Inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(s.Siblings);
        s.Responses.Setup(x => x.AddAsync(It.IsAny<ValuationOfficeResponse>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        s.UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var command = new SubmitOfficeResponseCommand(s.Target.Id, s.TargetActorId, 100000m, null);
        await s.Handler.Handle(command, CancellationToken.None);

        Assert.Equal(ValuationInquiryStatus.AwaitingOfficeResponses, s.Inquiry.Status);
    }

    [Fact]
    public async Task Handle_ThirdOfThreeResponsesArrives_CompletesInquiry_AndNotifiesRequester()
    {
        var s = BuildMultiInvitationScenario(totalInvitations: 3, alreadyResponded: 2, withRequester: true);

        SetupHappyPathReads(s.Invitations, s.Inquiries, s.Agencies, s.Target, s.Inquiry, s.TargetActorId, s.TargetAgencyId);
        s.Invitations.Setup(x => x.GetByInquiryIdAsync(s.Inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(s.Siblings);
        s.Inquiries.Setup(x => x.Update(s.Inquiry));
        s.Responses.Setup(x => x.AddAsync(It.IsAny<ValuationOfficeResponse>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        s.UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        s.UnitOfWork.Setup(x => x.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        s.Notifications.Setup(x => x.NotifyValuationResultReadyAsync(
                s.Inquiry.RequesterId!.Value, s.Inquiry.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var command = new SubmitOfficeResponseCommand(s.Target.Id, s.TargetActorId, 100000m, null);
        await s.Handler.Handle(command, CancellationToken.None);

        Assert.Equal(ValuationInquiryStatus.Completed, s.Inquiry.Status);
        // Twice: once for MarkCompleted, once more for MarkResultReadyNotified (Remediation
        // M3) after the notification above succeeds — two separate best-effort saves, not one
        // batched together with the required response save (see the handler's own comments).
        s.Inquiries.Verify(x => x.Update(s.Inquiry), Times.Exactly(2));
        s.Notifications.Verify(
            x => x.NotifyValuationResultReadyAsync(s.Inquiry.RequesterId!.Value, s.Inquiry.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_LastResponseCompletesInquiry_ButAnonymousInquiry_NeverNotifies()
    {
        // withRequester: false (default) -- RequesterId stays null, same guest rule
        // ValuationSlaEnforcementService already applies to ValuationInquiryExpired.
        var s = BuildMultiInvitationScenario(totalInvitations: 1, alreadyResponded: 0);

        SetupHappyPathReads(s.Invitations, s.Inquiries, s.Agencies, s.Target, s.Inquiry, s.TargetActorId, s.TargetAgencyId);
        s.Invitations.Setup(x => x.GetByInquiryIdAsync(s.Inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(s.Siblings);
        s.Inquiries.Setup(x => x.Update(s.Inquiry));
        s.Responses.Setup(x => x.AddAsync(It.IsAny<ValuationOfficeResponse>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        s.UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        s.UnitOfWork.Setup(x => x.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var command = new SubmitOfficeResponseCommand(s.Target.Id, s.TargetActorId, 100000m, null);
        await s.Handler.Handle(command, CancellationToken.None);

        Assert.Equal(ValuationInquiryStatus.Completed, s.Inquiry.Status);
        // Strict mock on `notifications` has no setup -- reaching NotifyValuationResultReadyAsync
        // for a null RequesterId would already throw.
    }

    [Fact]
    public async Task Handle_LastResponseCompletesInquiry_ButNotificationThrows_ResponseStillSucceeds()
    {
        // Remediation M3 — a notification failure must never undo, hide, or fail a response
        // that was already durably persisted.
        var s = BuildMultiInvitationScenario(totalInvitations: 1, alreadyResponded: 0, withRequester: true);

        SetupHappyPathReads(s.Invitations, s.Inquiries, s.Agencies, s.Target, s.Inquiry, s.TargetActorId, s.TargetAgencyId);
        s.Invitations.Setup(x => x.GetByInquiryIdAsync(s.Inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(s.Siblings);
        s.Inquiries.Setup(x => x.Update(s.Inquiry));
        s.Responses.Setup(x => x.AddAsync(It.IsAny<ValuationOfficeResponse>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        s.UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        s.UnitOfWork.Setup(x => x.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);
        s.Notifications.Setup(x => x.NotifyValuationResultReadyAsync(
                s.Inquiry.RequesterId!.Value, s.Inquiry.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient failure"));

        var command = new SubmitOfficeResponseCommand(s.Target.Id, s.TargetActorId, 100000m, null);
        var result = await s.Handler.Handle(command, CancellationToken.None);

        Assert.Equal(ValuationInquiryStatus.Completed, s.Inquiry.Status);
        Assert.Equal(s.Target.Id, result.InvitationId);
        s.Inquiries.Verify(x => x.Update(s.Inquiry), Times.Once);
    }

    [Fact]
    public async Task Handle_LastResponseArrives_ButLosesCompletionRaceToConcurrentWriter_ResponseStillSucceeds_NoNotificationSent()
    {
        // Remediation H1/H2 (Scenario C, unit-level): TrySaveChangesAsync returning false
        // models a concurrent sibling response (or the SLA sweep) having already won the race
        // to resolve this exact ValuationInquiry row via its xmin token. The office's own
        // response (Step 1, its own SaveChangesAsync) must still succeed regardless — this is
        // the whole reason the completion attempt is a SEPARATE, best-effort save rather than
        // being batched into the same one.
        var s = BuildMultiInvitationScenario(totalInvitations: 1, alreadyResponded: 0, withRequester: true);

        SetupHappyPathReads(s.Invitations, s.Inquiries, s.Agencies, s.Target, s.Inquiry, s.TargetActorId, s.TargetAgencyId);
        s.Invitations.Setup(x => x.GetByInquiryIdAsync(s.Inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(s.Siblings);
        s.Inquiries.Setup(x => x.Update(s.Inquiry));
        s.Responses.Setup(x => x.AddAsync(It.IsAny<ValuationOfficeResponse>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        s.UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        s.UnitOfWork.Setup(x => x.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var command = new SubmitOfficeResponseCommand(s.Target.Id, s.TargetActorId, 100000m, null);
        var result = await s.Handler.Handle(command, CancellationToken.None);

        // The response itself is unaffected by losing the completion race.
        Assert.Equal(s.Target.Id, result.InvitationId);
        Assert.Equal(ValuationOfficeInvitationStatus.Responded, s.Target.Status);
        // In-memory Status still reads Completed (MarkCompleted mutated it before the failed
        // save attempted) but nothing was actually persisted -- this handler does not overwrite
        // the aggregate again, and the next reader loads whatever the actual race winner wrote.
        // Strict mock on `notifications` has no setup -- since willComplete is false, reaching
        // NotifyValuationResultReadyAsync would already throw, proving no notification is sent
        // by the loser of the race.
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static (Guid AgencyId, Guid ActorId, ValuationOfficeInvitation Invitation, ValuationInquiry Inquiry) BuildScenario(
        bool invitationExpiresInFuture)
    {
        var agencyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();

        var createdAt = invitationExpiresInFuture ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(-25);
        var inquiry = ValuationInquiry.Create(governorateId: 1, requestType: ListingType.ForSale, utcNow: createdAt);
        inquiry.MarkAwaitingOfficeResponses(createdAt);

        var invitation = ValuationOfficeInvitation.Create(
            agencyId, inquiry.Id, ValuationMatchLevel.Neighborhood, createdAt);

        return (agencyId, actorId, invitation, inquiry);
    }

    private sealed class MultiInvitationScenario
    {
        public required SubmitOfficeResponseCommandHandler Handler { get; init; }
        public required Mock<IValuationOfficeInvitationRepository> Invitations { get; init; }
        public required Mock<IValuationOfficeResponseRepository> Responses { get; init; }
        public required Mock<IValuationInquiryRepository> Inquiries { get; init; }
        public required Mock<IAgencyRepository> Agencies { get; init; }
        public required Mock<INotificationService> Notifications { get; init; }
        public required Mock<IUnitOfWork> UnitOfWork { get; init; }
        public required ValuationInquiry Inquiry { get; init; }
        public required ValuationOfficeInvitation Target { get; init; }
        public required Guid TargetActorId { get; init; }
        public required Guid TargetAgencyId { get; init; }
        public required IReadOnlyList<ValuationOfficeInvitation> Siblings { get; init; }
    }

    /// <summary>
    /// Builds one inquiry with <paramref name="totalInvitations"/> invitations sent for it
    /// (mirroring OfficeMatchingService inviting however many offices it actually found), the
    /// first <paramref name="alreadyResponded"/> of which are already Responded. The command
    /// under test targets the next still-Sent invitation. Siblings is exactly what
    /// IValuationOfficeInvitationRepository.GetByInquiryIdAsync would return.
    /// </summary>
    private static MultiInvitationScenario BuildMultiInvitationScenario(
        int totalInvitations,
        int alreadyResponded,
        bool withRequester = false)
    {
        var createdAt = DateTime.UtcNow.AddHours(-1);
        var requesterId = withRequester ? Guid.NewGuid() : (Guid?)null;
        var inquiry = ValuationInquiry.Create(
            governorateId: 1, requestType: ListingType.ForSale, utcNow: createdAt, requesterId: requesterId);
        inquiry.MarkAwaitingOfficeResponses(createdAt);

        var siblings = new List<ValuationOfficeInvitation>();
        Guid? targetActorId = null;
        Guid? targetAgencyId = null;
        ValuationOfficeInvitation? target = null;

        for (var i = 0; i < totalInvitations; i++)
        {
            var agencyId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var invitation = ValuationOfficeInvitation.Create(
                agencyId, inquiry.Id, ValuationMatchLevel.Neighborhood, createdAt);

            if (i < alreadyResponded)
            {
                invitation.MarkResponded(createdAt.AddMinutes(5));
            }
            else if (target is null)
            {
                // The first still-Sent invitation is the one "arriving now" under test.
                target = invitation;
                targetActorId = actorId;
                targetAgencyId = agencyId;
            }

            siblings.Add(invitation);
        }

        var (handler, invitations, responses, inquiries, agencies, notifications, unitOfWork) = Build();

        return new MultiInvitationScenario
        {
            Handler = handler,
            Invitations = invitations,
            Responses = responses,
            Inquiries = inquiries,
            Agencies = agencies,
            Notifications = notifications,
            UnitOfWork = unitOfWork,
            Inquiry = inquiry,
            Target = target!,
            TargetActorId = targetActorId!.Value,
            TargetAgencyId = targetAgencyId!.Value,
            Siblings = siblings,
        };
    }

    private static void SetupHappyPathReads(
        Mock<IValuationOfficeInvitationRepository> invitations,
        Mock<IValuationInquiryRepository> inquiries,
        Mock<IAgencyRepository> agencies,
        ValuationOfficeInvitation invitation,
        ValuationInquiry inquiry,
        Guid actorId,
        Guid agencyId)
    {
        var actorAccount = BuildUserAccount(actorId, agencyId);

        invitations.Setup(x => x.GetByIdAsync(invitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        agencies.Setup(x => x.GetUserAccountAsync(actorId, It.IsAny<CancellationToken>())).ReturnsAsync(actorAccount);
        inquiries.Setup(x => x.GetByIdAsync(invitation.InquiryId, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);
        invitations.Setup(x => x.Update(invitation));
    }

    private static UserAccount BuildUserAccount(Guid id, Guid agencyId)
    {
        // UserAccount.Create takes the caller's own id directly (unlike Agency.Create, which
        // has no such parameter) — no reflection needed here. JoinAgency is the entity's own
        // public domain method for setting AgencyId.
        var account = UserAccount.Create(id, "Test", "User", DateTime.UtcNow);
        account.JoinAgency(agencyId, DateTime.UtcNow);
        return account;
    }

    private static (
        SubmitOfficeResponseCommandHandler Handler,
        Mock<IValuationOfficeInvitationRepository> Invitations,
        Mock<IValuationOfficeResponseRepository> Responses,
        Mock<IValuationInquiryRepository> Inquiries,
        Mock<IAgencyRepository> Agencies,
        Mock<INotificationService> Notifications,
        Mock<IUnitOfWork> UnitOfWork) Build()
    {
        var invitations = new Mock<IValuationOfficeInvitationRepository>(MockBehavior.Strict);
        var responses = new Mock<IValuationOfficeResponseRepository>(MockBehavior.Strict);
        var inquiries = new Mock<IValuationInquiryRepository>(MockBehavior.Strict);
        var agencies = new Mock<IAgencyRepository>(MockBehavior.Strict);
        var notifications = new Mock<INotificationService>(MockBehavior.Strict);
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);

        var handler = new SubmitOfficeResponseCommandHandler(
            invitations.Object,
            responses.Object,
            inquiries.Object,
            agencies.Object,
            notifications.Object,
            unitOfWork.Object,
            TimeProvider.System,
            NullLogger<SubmitOfficeResponseCommandHandler>.Instance);

        return (handler, invitations, responses, inquiries, agencies, notifications, unitOfWork);
    }
}
