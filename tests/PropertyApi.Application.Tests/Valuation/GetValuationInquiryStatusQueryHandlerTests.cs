using Moq;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Application.Valuation.Queries.GetValuationInquiryStatus;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Tests.Valuation;

/// <summary>
/// Stage 7 — GetValuationInquiryStatusQueryHandler's authorization rule: an inquiry tied to a
/// real account may only be viewed by that account; an anonymous inquiry has no account to
/// check, so its own id is the only access control. Also covers the estimates list only ever
/// including Responded invitations with an actual persisted response.
/// </summary>
public sealed class GetValuationInquiryStatusQueryHandlerTests
{
    [Fact]
    public async Task Handle_InquiryNotFound_ThrowsNotFound()
    {
        var (handler, inquiries, invitations, responses) = Build();
        var inquiryId = Guid.NewGuid();

        inquiries.Setup(x => x.GetByIdAsync(inquiryId, It.IsAny<CancellationToken>())).ReturnsAsync((ValuationInquiry?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new GetValuationInquiryStatusQuery(inquiryId, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_OwnedInquiry_ViewedByADifferentUser_ThrowsForbidden()
    {
        var (handler, inquiries, invitations, responses) = Build();
        var requesterId = Guid.NewGuid();
        var inquiry = BuildInquiry(requesterId);

        inquiries.Setup(x => x.GetByIdAsync(inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => handler.Handle(new GetValuationInquiryStatusQuery(inquiry.Id, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_OwnedInquiry_ViewedByAnAnonymousCaller_ThrowsForbidden()
    {
        var (handler, inquiries, invitations, responses) = Build();
        var requesterId = Guid.NewGuid();
        var inquiry = BuildInquiry(requesterId);

        inquiries.Setup(x => x.GetByIdAsync(inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => handler.Handle(new GetValuationInquiryStatusQuery(inquiry.Id, ActorUserId: null), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AnonymousInquiry_ViewedByAnyoneIncludingAnAnonymousCaller_Succeeds()
    {
        var (handler, inquiries, invitations, responses) = Build();
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
        var (handler, inquiries, invitations, responses) = Build();
        var inquiry = BuildInquiry(requesterId: null);
        inquiry.MarkAwaitingOfficeResponses(DateTime.UtcNow);

        var respondedInvitation = ValuationOfficeInvitation.Create(Guid.NewGuid(), inquiry.Id, ValuationMatchLevel.Neighborhood, DateTime.UtcNow);
        respondedInvitation.MarkResponded(DateTime.UtcNow);

        var stillSentInvitation = ValuationOfficeInvitation.Create(Guid.NewGuid(), inquiry.Id, ValuationMatchLevel.District, DateTime.UtcNow);

        var response = ValuationOfficeResponse.Create(respondedInvitation.Id, 175000m, DateTime.UtcNow, "ملاحظة");

        inquiries.Setup(x => x.GetByIdAsync(inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);
        invitations.Setup(x => x.GetByInquiryIdAsync(inquiry.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([respondedInvitation, stillSentInvitation]);
        responses.Setup(x => x.GetByInvitationIdAsync(respondedInvitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(response);
        // No setup for stillSentInvitation on the Strict `responses` mock -- the handler must
        // never even ask for a response for an invitation that is not Responded.

        var result = await handler.Handle(new GetValuationInquiryStatusQuery(inquiry.Id, ActorUserId: null), CancellationToken.None);

        var estimate = Assert.Single(result.Estimates);
        Assert.Equal(175000m, estimate.EstimatedPrice);
        Assert.Equal("ملاحظة", estimate.Notes);
        Assert.Equal(ValuationMatchLevel.Neighborhood, estimate.MatchLevel);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static ValuationInquiry BuildInquiry(Guid? requesterId)
        => ValuationInquiry.Create(
            governorateId: 1, requestType: ListingType.ForSale, utcNow: DateTime.UtcNow, requesterId: requesterId);

    private static (
        GetValuationInquiryStatusQueryHandler Handler,
        Mock<IValuationInquiryRepository> Inquiries,
        Mock<IValuationOfficeInvitationRepository> Invitations,
        Mock<IValuationOfficeResponseRepository> Responses) Build()
    {
        var inquiries = new Mock<IValuationInquiryRepository>(MockBehavior.Strict);
        var invitations = new Mock<IValuationOfficeInvitationRepository>(MockBehavior.Strict);
        var responses = new Mock<IValuationOfficeResponseRepository>(MockBehavior.Strict);

        var handler = new GetValuationInquiryStatusQueryHandler(inquiries.Object, invitations.Object, responses.Object);

        return (handler, inquiries, invitations, responses);
    }
}
