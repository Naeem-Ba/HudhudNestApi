using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Application.Valuation.Queries.GetMyAgencyValuationInquiries;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Tests.Valuation;

/// <summary>
/// Stage 6 — GetMyAgencyValuationInquiriesQueryHandler: resolving "my agency" server-side
/// (never from a client-supplied agency id), and the privacy rule that the dashboard DTO must
/// never expose ValuationInquiry.RequesterId — enforced structurally by
/// ValuationOfficeInvitationDashboardDtoTests (Architecture.Tests) reflecting over the DTO
/// type itself, and behaviourally here by never wiring RequesterId into the DTO the handler
/// builds.
/// </summary>
public sealed class GetMyAgencyValuationInquiriesQueryHandlerTests
{
    [Fact]
    public async Task Handle_NoAccountFound_ThrowsNotFound()
    {
        var (handler, agencies, invitations, inquiries, responses) = Build();
        var actorId = Guid.NewGuid();

        agencies.Setup(x => x.GetUserAccountAsync(actorId, It.IsAny<CancellationToken>())).ReturnsAsync((UserAccount?)null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new GetMyAgencyValuationInquiriesQuery(actorId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AccountHasNoAgency_ThrowsNotFound()
    {
        var (handler, agencies, invitations, inquiries, responses) = Build();
        var actorId = Guid.NewGuid();
        var independentAccount = UserAccount.Create(actorId, "Test", "User", DateTime.UtcNow); // AgencyId stays null

        agencies.Setup(x => x.GetUserAccountAsync(actorId, It.IsAny<CancellationToken>())).ReturnsAsync(independentAccount);

        await Assert.ThrowsAsync<NotFoundException>(
            () => handler.Handle(new GetMyAgencyValuationInquiriesQuery(actorId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AgencyWithNoInvitations_ReturnsEmptyList()
    {
        var (handler, agencies, invitations, inquiries, responses) = Build();
        var actorId = Guid.NewGuid();
        var agencyId = Guid.NewGuid();
        var account = BuildMember(actorId, agencyId);

        agencies.Setup(x => x.GetUserAccountAsync(actorId, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        invitations.Setup(x => x.GetByAgencyIdAsync(agencyId, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var result = await handler.Handle(new GetMyAgencyValuationInquiriesQuery(actorId), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyTheCallersOwnAgencyInvitations_EnrichedWithInquiryAndOwnResponse()
    {
        var (handler, agencies, invitations, inquiries, responses) = Build();
        var actorId = Guid.NewGuid();
        var agencyId = Guid.NewGuid();
        var account = BuildMember(actorId, agencyId);

        var createdAt = DateTime.UtcNow.AddHours(-2);
        var inquiry = ValuationInquiry.Create(
            governorateId: 5, requestType: ListingType.ForRent, utcNow: createdAt,
            propertyTypeId: 2, area: 120m, rooms: 3, districtId: 10, neighborhoodId: 100);
        inquiry.MarkAwaitingOfficeResponses(createdAt);

        var invitation = ValuationOfficeInvitation.Create(agencyId, inquiry.Id, ValuationMatchLevel.District, createdAt);
        var response = ValuationOfficeResponse.Create(invitation.Id, 250000m, DateTime.UtcNow, "تقدير أولي");

        agencies.Setup(x => x.GetUserAccountAsync(actorId, It.IsAny<CancellationToken>())).ReturnsAsync(account);
        invitations.Setup(x => x.GetByAgencyIdAsync(agencyId, It.IsAny<CancellationToken>())).ReturnsAsync([invitation]);
        inquiries.Setup(x => x.GetByIdAsync(inquiry.Id, It.IsAny<CancellationToken>())).ReturnsAsync(inquiry);
        responses.Setup(x => x.GetByInvitationIdAsync(invitation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(response);

        var result = await handler.Handle(new GetMyAgencyValuationInquiriesQuery(actorId), CancellationToken.None);

        var dto = Assert.Single(result);
        Assert.Equal(invitation.Id, dto.InvitationId);
        Assert.Equal(inquiry.Id, dto.InquiryId);
        Assert.Equal(ValuationMatchLevel.District, dto.MatchLevel);
        Assert.Equal(5, dto.GovernorateId);
        Assert.Equal(10, dto.DistrictId);
        Assert.Equal(120m, dto.Area);
        Assert.Equal(3, dto.Rooms);
        Assert.Equal(250000m, dto.MyEstimatedPrice);
        Assert.Equal("تقدير أولي", dto.MyNotes);
        // CanRespond: Status is still Sent and the inquiry has not hit its 24h ExpiresAt yet.
        Assert.True(dto.CanRespond);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static UserAccount BuildMember(Guid id, Guid agencyId)
    {
        var account = UserAccount.Create(id, "Test", "User", DateTime.UtcNow);
        account.JoinAgency(agencyId, DateTime.UtcNow);
        return account;
    }

    private static (
        GetMyAgencyValuationInquiriesQueryHandler Handler,
        Mock<IAgencyRepository> Agencies,
        Mock<IValuationOfficeInvitationRepository> Invitations,
        Mock<IValuationInquiryRepository> Inquiries,
        Mock<IValuationOfficeResponseRepository> Responses) Build()
    {
        var agencies = new Mock<IAgencyRepository>(MockBehavior.Strict);
        var invitations = new Mock<IValuationOfficeInvitationRepository>(MockBehavior.Strict);
        var inquiries = new Mock<IValuationInquiryRepository>(MockBehavior.Strict);
        var responses = new Mock<IValuationOfficeResponseRepository>(MockBehavior.Strict);

        var handler = new GetMyAgencyValuationInquiriesQueryHandler(
            agencies.Object, invitations.Object, inquiries.Object, responses.Object);

        return (handler, agencies, invitations, inquiries, responses);
    }
}
