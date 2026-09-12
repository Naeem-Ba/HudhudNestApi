using Moq;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Application.Valuation.Queries.GetValuationInquiryStatus;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Tests.Valuation;

/// <summary>
/// Stage 7 — GetValuationInquiryStatusQueryHandler's authorization rule: an inquiry tied to a
/// real account may only be viewed by that account; an anonymous inquiry has no account to
/// check, so its own id is the only access control. Also covers the estimates list only ever
/// including Responded invitations with an actual persisted response.
///
/// Stage 9 additions: each estimate now also carries InvitationId/AgencyId/AgencyName (so the
/// customer can pick one to consent to) and ContactConsentGiven (whether
/// SubmitValuationContactConsentCommand already succeeded for it).
/// </summary>
public sealed class GetValuationInquiryStatusQueryHandlerTests
{
    [Fact]
    public async Task Handle_InquiryNotFound_ThrowsNotFound()
    {
        var (handler, inquiries, invitations, responses, consents, agencies) = Build();
        var inquiryId = Guid.NewGuid();

        inquiries.Setup(x => x.GetByIdAsync(inquiryId, It.IsAny<CancellationToken>())).ReturnsAsync((ValuationInquiry?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new GetValuationInquiryStatusQuery(inquiryId, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_OwnedInquiry_ViewedByADifferentUser_ThrowsForbidden()
    {
        var (handler, inquiries, invitations, responses, consents, agencies) = Build();
        var requesterId = Guid.NewGuid();
        var inquiry = BuildInquiry(requesterId);

        inquiries.Setup(x => x.GetByIdAsync(inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => handler.Handle(new GetValuationInquiryStatusQuery(inquiry.Id, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_OwnedInquiry_ViewedByAnAnonymousCaller_ThrowsForbidden()
    {
        var (handler, inquiries, invitations, responses, consents, agencies) = Build();
        var requesterId = Guid.NewGuid();
        var inquiry = BuildInquiry(requesterId);

        inquiries.Setup(x => x.GetByIdAsync(inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => handler.Handle(new GetValuationInquiryStatusQuery(inquiry.Id, ActorUserId: null), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AnonymousInquiry_ViewedByAnyoneIncludingAnAnonymousCaller_Succeeds()
    {
        var (handler, inquiries, invitations, responses, consents, agencies) = Build();
        var inquiry = BuildInquiry(requesterId: null);

        inquiries.Setup(x => x.GetByIdAsync(inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);
        invitations.Setup(x => x.GetByInquiryIdAsync(inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await handler.Handle(new GetValuationInquiryStatusQuery(inquiry.Id, ActorUserId: null), CancellationToken.None);

        Assert.Equal(inquiry.Id, result.InquiryId);
        Assert.Empty(result.Estimates);
    }

    [Fact]
    public async Task Handle_OnlyIncludesRespondedInvitationsWithAPersistedResponse()
    {
        var (handler, inquiries, invitations, responses, consents, agencies) = Build();
        var inquiry = BuildInquiry(requesterId: null);
        inquiry.MarkAwaitingOfficeResponses(DateTime.UtcNow);

        var agency = Agency.Create("مكتب الأمين", "al-amin", Guid.NewGuid(), "SY", DateTime.UtcNow);
        var respondedInvitation = ValuationOfficeInvitation.Create(agency.Id, inquiry.Id, ValuationMatchLevel.Neighborhood, DateTime.UtcNow);
        respondedInvitation.MarkResponded(DateTime.UtcNow);

        var stillSentInvitation = ValuationOfficeInvitation.Create(Guid.NewGuid(), inquiry.Id, ValuationMatchLevel.District, DateTime.UtcNow);

        var response = ValuationOfficeResponse.Create(respondedInvitation.Id, 175000m, DateTime.UtcNow, "ملاحظة");

        inquiries.Setup(x => x.GetByIdAsync(inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);
        invitations.Setup(x => x.GetByInquiryIdAsync(inquiry.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([respondedInvitation, stillSentInvitation]);
        responses.Setup(x => x.GetByInvitationIdAsync(respondedInvitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(response);
        // No setup for stillSentInvitation on the Strict `responses` mock -- the handler must
        // never even ask for a response for an invitation that is not Responded.
        consents.Setup(x => x.GetByInvitationIdAsync(respondedInvitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync((ValuationContactConsent?)null);
        agencies.Setup(x => x.GetByIdsAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Single() == agency.Id), It.IsAny<CancellationToken>()))
            .ReturnsAsync([agency]);

        var result = await handler.Handle(new GetValuationInquiryStatusQuery(inquiry.Id, ActorUserId: null), CancellationToken.None);

        var estimate = Assert.Single(result.Estimates);
        Assert.Equal(respondedInvitation.Id, estimate.InvitationId);
        Assert.Equal(agency.Id, estimate.AgencyId);
        Assert.Equal("مكتب الأمين", estimate.AgencyName);
        Assert.Equal(175000m, estimate.EstimatedPrice);
        Assert.Equal("ملاحظة", estimate.Notes);
        Assert.Equal(ValuationMatchLevel.Neighborhood, estimate.MatchLevel);
        Assert.False(estimate.ContactConsentGiven);
    }

    [Fact]
    public async Task Handle_RespondedInvitationWithExistingConsent_MarksContactConsentGiven()
    {
        var (handler, inquiries, invitations, responses, consents, agencies) = Build();
        var inquiry = BuildInquiry(requesterId: null);
        inquiry.MarkAwaitingOfficeResponses(DateTime.UtcNow);

        var agency = Agency.Create("مكتب", "office", Guid.NewGuid(), "SY", DateTime.UtcNow);
        var invitation = ValuationOfficeInvitation.Create(agency.Id, inquiry.Id, ValuationMatchLevel.Neighborhood, DateTime.UtcNow);
        invitation.MarkResponded(DateTime.UtcNow);
        var response = ValuationOfficeResponse.Create(invitation.Id, 100_000m, DateTime.UtcNow);
        var consent = ValuationContactConsent.Create(inquiry.Id, invitation.Id, agency.Id, "0991234567", null, DateTime.UtcNow);

        inquiries.Setup(x => x.GetByIdAsync(inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);
        invitations.Setup(x => x.GetByInquiryIdAsync(inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync([invitation]);
        responses.Setup(x => x.GetByInvitationIdAsync(invitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(response);
        consents.Setup(x => x.GetByInvitationIdAsync(invitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(consent);
        agencies.Setup(x => x.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([agency]);

        var result = await handler.Handle(new GetValuationInquiryStatusQuery(inquiry.Id, ActorUserId: null), CancellationToken.None);

        Assert.True(Assert.Single(result.Estimates).ContactConsentGiven);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static ValuationInquiry BuildInquiry(Guid? requesterId)
        => ValuationInquiry.Create(
            governorateId: 1, requestType: ListingType.ForSale, utcNow: DateTime.UtcNow, requesterId: requesterId);

    private static (
        GetValuationInquiryStatusQueryHandler Handler,
        Mock<IValuationInquiryRepository> Inquiries,
        Mock<IValuationOfficeInvitationRepository> Invitations,
        Mock<IValuationOfficeResponseRepository> Responses,
        Mock<IValuationContactConsentRepository> Consents,
        Mock<IAgencyRepository> Agencies) Build()
    {
        var inquiries = new Mock<IValuationInquiryRepository>(MockBehavior.Strict);
        var invitations = new Mock<IValuationOfficeInvitationRepository>(MockBehavior.Strict);
        var responses = new Mock<IValuationOfficeResponseRepository>(MockBehavior.Strict);
        var consents = new Mock<IValuationContactConsentRepository>(MockBehavior.Strict);
        var agencies = new Mock<IAgencyRepository>(MockBehavior.Strict);

        var handler = new GetValuationInquiryStatusQueryHandler(
            inquiries.Object, invitations.Object, responses.Object, consents.Object, agencies.Object);

        return (handler, inquiries, invitations, responses, consents, agencies);
    }
}
