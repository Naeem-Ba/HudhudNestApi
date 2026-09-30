using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Valuation.Commands.SubmitValuationContactConsent;
using HudhudNestApi.Application.Valuation.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Valuation.Entities;
using HudhudNestApi.Domain.Valuation.Enums;

namespace HudhudNestApi.Application.Tests.Valuation;

/// <summary>
/// Stage 9 — SubmitValuationContactConsentCommandHandler: ownership, the cross-inquiry IDOR
/// guard (an invitation id that belongs to someone ELSE's inquiry must be rejected even though
/// the caller legitimately owns request.InquiryId), the "must have a response first" rule, and
/// idempotency (resubmitting the same invitation's consent never creates a duplicate row).
/// </summary>
public sealed class SubmitValuationContactConsentCommandHandlerTests
{
    [Fact]
    public async Task Handle_InquiryNotFound_ThrowsNotFound()
    {
        var (handler, inquiries, invitations, consents, unitOfWork) = Build();
        var inquiryId = Guid.NewGuid();

        inquiries.Setup(x => x.GetByIdAsync(inquiryId, It.IsAny<CancellationToken>())).ReturnsAsync((ValuationInquiry?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new SubmitValuationContactConsentCommand(inquiryId, Guid.NewGuid(), Guid.NewGuid(), "0991234567", null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_OwnedInquiry_CalledByADifferentUser_ThrowsForbidden()
    {
        var (handler, inquiries, invitations, consents, unitOfWork) = Build();
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, DateTime.UtcNow, requesterId: Guid.NewGuid());

        inquiries.Setup(x => x.GetByIdAsync(inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new SubmitValuationContactConsentCommand(inquiry.Id, Guid.NewGuid(), Guid.NewGuid(), "0991234567", null),
            CancellationToken.None));
    }

    /// <summary>
    /// The exact IDOR case this handler exists to close: the caller legitimately owns
    /// `inquiry`, but supplies an InvitationId that actually belongs to a completely different
    /// inquiry. Owning one inquiry must never grant any authority over another's invitations.
    /// </summary>
    [Fact]
    public async Task Handle_InvitationBelongsToADifferentInquiry_ThrowsNotFound()
    {
        var (handler, inquiries, invitations, consents, unitOfWork) = Build();
        var requesterId = Guid.NewGuid();
        var myInquiry = ValuationInquiry.Create(1, ListingType.ForSale, DateTime.UtcNow, requesterId: requesterId);

        var someoneElsesInquiryId = Guid.NewGuid();
        var foreignInvitation = ValuationOfficeInvitation.Create(
            Guid.NewGuid(), someoneElsesInquiryId, ValuationMatchLevel.Neighborhood, DateTime.UtcNow);
        foreignInvitation.MarkResponded(DateTime.UtcNow);

        inquiries.Setup(x => x.GetByIdAsync(myInquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(myInquiry);
        invitations.Setup(x => x.GetByIdAsync(foreignInvitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(foreignInvitation);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new SubmitValuationContactConsentCommand(myInquiry.Id, foreignInvitation.Id, requesterId, "0991234567", null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_InvitationDoesNotExist_ThrowsNotFound()
    {
        var (handler, inquiries, invitations, consents, unitOfWork) = Build();
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, DateTime.UtcNow);
        var invitationId = Guid.NewGuid();

        inquiries.Setup(x => x.GetByIdAsync(inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);
        invitations.Setup(x => x.GetByIdAsync(invitationId, It.IsAny<CancellationToken>())).ReturnsAsync((ValuationOfficeInvitation?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new SubmitValuationContactConsentCommand(inquiry.Id, invitationId, null, "0991234567", null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_InvitationStillSent_NoResponseYet_ThrowsConflict()
    {
        var (handler, inquiries, invitations, consents, unitOfWork) = Build();
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, DateTime.UtcNow);
        inquiry.MarkAwaitingOfficeResponses(DateTime.UtcNow);
        var invitation = ValuationOfficeInvitation.Create(Guid.NewGuid(), inquiry.Id, ValuationMatchLevel.Neighborhood, DateTime.UtcNow);

        inquiries.Setup(x => x.GetByIdAsync(inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);
        invitations.Setup(x => x.GetByIdAsync(invitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        consents.Setup(x => x.GetByInvitationIdAsync(invitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync((ValuationContactConsent?)null);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new SubmitValuationContactConsentCommand(inquiry.Id, invitation.Id, null, "0991234567", null),
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_RespondedInvitation_WithContactPhone_CreatesConsent()
    {
        var (handler, inquiries, invitations, consents, unitOfWork) = Build();
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, DateTime.UtcNow);
        inquiry.MarkAwaitingOfficeResponses(DateTime.UtcNow);
        var agencyId = Guid.NewGuid();
        var invitation = ValuationOfficeInvitation.Create(agencyId, inquiry.Id, ValuationMatchLevel.Neighborhood, DateTime.UtcNow);
        invitation.MarkResponded(DateTime.UtcNow);

        inquiries.Setup(x => x.GetByIdAsync(inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);
        invitations.Setup(x => x.GetByIdAsync(invitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        consents.Setup(x => x.GetByInvitationIdAsync(invitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync((ValuationContactConsent?)null);
        consents.Setup(x => x.AddAsync(It.IsAny<ValuationContactConsent>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var result = await handler.Handle(
            new SubmitValuationContactConsentCommand(inquiry.Id, invitation.Id, null, "0991234567", null),
            CancellationToken.None);

        Assert.Equal(invitation.Id, result.InvitationId);
        consents.Verify(x => x.AddAsync(
            It.Is<ValuationContactConsent>(c =>
                c.InquiryId == inquiry.Id && c.InvitationId == invitation.Id && c.AgencyId == agencyId
                && c.ContactPhone == "0991234567" && c.ContactEmail == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Idempotency: a second submission for the same invitation must not create a
    /// second row, and must not even check the invitation's response status again (it returns
    /// the existing consent immediately) — matches this module's own idempotency rule.</summary>
    [Fact]
    public async Task Handle_ConsentAlreadyExistsForThisInvitation_ReturnsExistingConsent_NeverDuplicates()
    {
        var (handler, inquiries, invitations, consents, unitOfWork) = Build();
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, DateTime.UtcNow);
        var invitation = ValuationOfficeInvitation.Create(Guid.NewGuid(), inquiry.Id, ValuationMatchLevel.Neighborhood, DateTime.UtcNow);
        // Deliberately still Sent (not Responded) -- proves the handler short-circuits on the
        // existing-consent check BEFORE reaching the "must be Responded" guard.
        var existingConsent = ValuationContactConsent.Create(
            inquiry.Id, invitation.Id, invitation.AgencyId, "0991234567", null, DateTime.UtcNow.AddMinutes(-10));

        inquiries.Setup(x => x.GetByIdAsync(inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);
        invitations.Setup(x => x.GetByIdAsync(invitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(invitation);
        consents.Setup(x => x.GetByInvitationIdAsync(invitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(existingConsent);

        var result = await handler.Handle(
            new SubmitValuationContactConsentCommand(inquiry.Id, invitation.Id, null, "0999999999", null),
            CancellationToken.None);

        Assert.Equal(existingConsent.ConsentedAt, result.ConsentedAt);
        // Strict mocks on `consents.AddAsync`/`unitOfWork.SaveChangesAsync` have no setup --
        // reaching either would already throw.
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static (
        SubmitValuationContactConsentCommandHandler Handler,
        Mock<IValuationInquiryRepository> Inquiries,
        Mock<IValuationOfficeInvitationRepository> Invitations,
        Mock<IValuationContactConsentRepository> Consents,
        Mock<IUnitOfWork> UnitOfWork) Build()
    {
        var inquiries = new Mock<IValuationInquiryRepository>(MockBehavior.Strict);
        var invitations = new Mock<IValuationOfficeInvitationRepository>(MockBehavior.Strict);
        var consents = new Mock<IValuationContactConsentRepository>(MockBehavior.Strict);
        var unitOfWork = new Mock<IUnitOfWork>(MockBehavior.Strict);

        var handler = new SubmitValuationContactConsentCommandHandler(
            inquiries.Object,
            invitations.Object,
            consents.Object,
            unitOfWork.Object,
            NullLogger<SubmitValuationContactConsentCommandHandler>.Instance);

        return (handler, inquiries, invitations, consents, unitOfWork);
    }
}
