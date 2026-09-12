using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
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
/// </summary>
public sealed class SubmitOfficeResponseCommandHandlerTests
{
    [Fact]
    public async Task Handle_ValidResponse_MarksInvitationResponded_AndPersistsTheEstimate()
    {
        var (agencyId, actorId, invitation, inquiry) = BuildScenario(invitationExpiresInFuture: true);
        var (handler, invitations, responses, inquiries, agencies, unitOfWork) = Build();

        SetupHappyPathReads(invitations, inquiries, agencies, invitation, inquiry, actorId, agencyId);
        responses.Setup(x => x.AddAsync(It.IsAny<ValuationOfficeResponse>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var command = new SubmitOfficeResponseCommand(invitation.Id, actorId, 150000m, "ملاحظة اختبارية");

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(invitation.Id, result.InvitationId);
        Assert.Equal(150000m, result.EstimatedPrice);
        Assert.Equal("ملاحظة اختبارية", result.Notes);
        Assert.Equal(ValuationOfficeInvitationStatus.Responded, invitation.Status);
        invitations.Verify(x => x.Update(invitation), Times.Once);
        responses.Verify(x => x.AddAsync(It.Is<ValuationOfficeResponse>(r => r.InvitationId == invitation.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_CallersAgencyDoesNotMatchInvitation_ThrowsForbidden_AndNeverTouchesTheInvitation()
    {
        var (agencyId, actorId, invitation, inquiry) = BuildScenario(invitationExpiresInFuture: true);
        var (handler, invitations, responses, inquiries, agencies, unitOfWork) = Build();

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
        var (handler, invitations, responses, inquiries, agencies, unitOfWork) = Build();

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
        // Strict mock on `responses` has no AddAsync setup -- reaching it would already throw;
        // this is redundant confirmation that no response was ever created for an expired
        // invitation.
    }

    [Fact]
    public async Task Handle_InvitationAlreadyResponded_ThrowsConflict_NotDuplicated()
    {
        var (agencyId, actorId, invitation, inquiry) = BuildScenario(invitationExpiresInFuture: true);
        invitation.MarkResponded(DateTime.UtcNow.AddMinutes(-5)); // already answered once

        var (handler, invitations, responses, inquiries, agencies, unitOfWork) = Build();
        var actorAccount = BuildUserAccount(actorId, agencyId);

        invitations.Setup(x => x.GetByIdAsync(invitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        agencies.Setup(x => x.GetUserAccountAsync(actorId, It.IsAny<CancellationToken>())).ReturnsAsync(actorAccount);
        inquiries.Setup(x => x.GetByIdAsync(invitation.InquiryId, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);

        var command = new SubmitOfficeResponseCommand(invitation.Id, actorId, 100000m, null);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(command, CancellationToken.None));
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
        Mock<IUnitOfWork> UnitOfWork) Build()
    {
        var invitations = new Mock<IValuationOfficeInvitationRepository>(MockBehavior.Strict);
        var responses = new Mock<IValuationOfficeResponseRepository>(MockBehavior.Strict);
        var inquiries = new Mock<IValuationInquiryRepository>(MockBehavior.Strict);
        var agencies = new Mock<IAgencyRepository>(MockBehavior.Strict);
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);

        var handler = new SubmitOfficeResponseCommandHandler(
            invitations.Object,
            responses.Object,
            inquiries.Object,
            agencies.Object,
            unitOfWork.Object,
            NullLogger<SubmitOfficeResponseCommandHandler>.Instance);

        return (handler, invitations, responses, inquiries, agencies, unitOfWork);
    }
}
