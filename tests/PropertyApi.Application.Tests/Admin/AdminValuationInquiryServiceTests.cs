using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PropertyApi.Application.Admin.Services;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.Valuation.DTOs;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Audit.Constants;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Tests.Admin;

/// <summary>
/// Stage 8 (Admin Dashboard) — unit-level proof of AdminValuationInquiryService's own
/// arithmetic and control flow with every repository mocked. The "does this match what's
/// actually in the database" claim itself is proven separately, at the Integration level (see
/// AdminValuationDashboardIntegrationTests), per this stage's own explicit requirement not to
/// rely only on mocks for the statistics math.
/// </summary>
public sealed class AdminValuationInquiryServiceTests
{
    private static readonly Guid AdminId = Guid.NewGuid();

    [Fact]
    public async Task GetInquiriesAsync_ClampsPaging_AndParsesStatusFilter()
    {
        var inquiry = ValuationInquiry.Create(1, ListingType.ForSale, DateTime.UtcNow, requesterId: Guid.NewGuid());

        var inquiries = new Mock<IValuationInquiryRepository>();
        inquiries
            .Setup(x => x.GetPagedAsync(ValuationInquiryStatus.Pending, 1, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ValuationInquiry>
            {
                Items = [inquiry],
                TotalCount = 1,
                Page = 1,
                PageSize = 100
            });

        var service = BuildService(inquiries: inquiries.Object);

        // page=0 and pageSize=500 are both out of range on purpose — must clamp exactly like
        // AdminListingService.GetUserPropertiesAsync does.
        var result = await service.GetInquiriesAsync(0, 500, "pending", CancellationToken.None);

        Assert.Equal(1, result.TotalCount);
        Assert.Single(result.Items);
        Assert.Equal(inquiry.Id, result.Items[0].Id);
        Assert.Equal("Pending", result.Items[0].Status);
        Assert.Equal("ForSale", result.Items[0].RequestType);

        inquiries.Verify(x => x.GetPagedAsync(ValuationInquiryStatus.Pending, 1, 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetInquiriesAsync_WithUnknownStatusText_PassesNullFilterThrough()
    {
        var inquiries = new Mock<IValuationInquiryRepository>();
        inquiries
            .Setup(x => x.GetPagedAsync(null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<ValuationInquiry> { Items = [], TotalCount = 0, Page = 1, PageSize = 20 });

        var service = BuildService(inquiries: inquiries.Object);

        await service.GetInquiriesAsync(1, 20, "not-a-real-status", CancellationToken.None);

        inquiries.Verify(x => x.GetPagedAsync(null, 1, 20, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The exact rule the spec calls out by name: ResponseRate must be a real ratio (here,
    /// 2/4 = 0.5), and SlaComplianceRate is computed independently from its own
    /// ResponsesWithinSla figure (here, 1/4 = 0.25) rather than being aliased to ResponseRate —
    /// they diverge in this test specifically to prove neither is silently copied from the
    /// other.
    /// </summary>
    [Fact]
    public async Task GetOfficeStatisticsAsync_ComputesIndependentRates_NotJustAResponseCount()
    {
        var agencyId = Guid.NewGuid();
        var agency = Agency.Create("مكتب الأمين", "al-amin", Guid.NewGuid(), "SY", DateTime.UtcNow);
        SetAgencyId(agency, agencyId);

        var invitations = new Mock<IValuationOfficeInvitationRepository>();
        invitations
            .Setup(x => x.GetInvitationCountsByAgencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ValuationOfficeInvitationCountsRow(agencyId, TotalInvitations: 4, TotalResponses: 2)]);

        var responses = new Mock<IValuationOfficeResponseRepository>();
        responses
            .Setup(x => x.GetWithinSlaResponseCountsByAgencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [agencyId] = 1 });

        var agencies = new Mock<IAgencyRepository>();
        agencies
            .Setup(x => x.GetByIdsAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(agencyId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync([agency]);

        var service = BuildService(
            invitations: invitations.Object,
            responses: responses.Object,
            agencies: agencies.Object);

        var stats = await service.GetOfficeStatisticsAsync(CancellationToken.None);

        var row = Assert.Single(stats);
        Assert.Equal("مكتب الأمين", row.AgencyName);
        Assert.Equal(4, row.TotalInvitations);
        Assert.Equal(2, row.TotalResponses);
        Assert.Equal(1, row.ResponsesWithinSla);
        Assert.Equal(0.5m, row.ResponseRate);
        Assert.Equal(0.25m, row.SlaComplianceRate);
        Assert.NotEqual(row.ResponseRate, row.SlaComplianceRate);
    }

    [Fact]
    public async Task GetOfficeStatisticsAsync_NoInvitationsAnywhere_ReturnsEmptyWithoutQueryingAgencies()
    {
        var invitations = new Mock<IValuationOfficeInvitationRepository>();
        invitations
            .Setup(x => x.GetInvitationCountsByAgencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var agencies = new Mock<IAgencyRepository>();

        var service = BuildService(invitations: invitations.Object, agencies: agencies.Object);

        var stats = await service.GetOfficeStatisticsAsync(CancellationToken.None);

        Assert.Empty(stats);
        agencies.Verify(x => x.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetOfficeStatisticsAsync_AgencySinceDeleted_StillReturnsRow_WithFallbackName()
    {
        var agencyId = Guid.NewGuid();

        var invitations = new Mock<IValuationOfficeInvitationRepository>();
        invitations
            .Setup(x => x.GetInvitationCountsByAgencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ValuationOfficeInvitationCountsRow(agencyId, TotalInvitations: 3, TotalResponses: 0)]);

        var responses = new Mock<IValuationOfficeResponseRepository>();
        responses
            .Setup(x => x.GetWithinSlaResponseCountsByAgencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());

        var agencies = new Mock<IAgencyRepository>();
        agencies
            .Setup(x => x.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]); // agency row no longer exists

        var service = BuildService(invitations: invitations.Object, responses: responses.Object, agencies: agencies.Object);

        var stats = await service.GetOfficeStatisticsAsync(CancellationToken.None);

        var row = Assert.Single(stats);
        Assert.Equal("(محذوف)", row.AgencyName);
        Assert.Equal(0m, row.ResponseRate);
        Assert.False(row.RequiresManualReview);
    }

    [Fact]
    public async Task FlagOfficeForReviewAsync_Succeeds_SetsFlag_AndAuditLogs()
    {
        var agency = Agency.Create("مكتب بطيء", "slow-office", Guid.NewGuid(), "SY", DateTime.UtcNow);

        var agencies = new Mock<IAgencyRepository>();
        agencies.Setup(x => x.GetByIdAsync(agency.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agency);

        var auditLogs = new Mock<IAuditLogService>();
        var service = BuildService(agencies: agencies.Object, auditLogs: auditLogs.Object);

        var result = await service.FlagOfficeForReviewAsync(
            agency.Id, "بطء متكرر في الرد", AdminId, "127.0.0.1", CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True(agency.RequiresManualReview);
        Assert.Equal("بطء متكرر في الرد", agency.ManualReviewReason);
        Assert.NotNull(agency.ManualReviewFlaggedAt);

        auditLogs.Verify(x => x.LogAsync(
            AdminId, AuditActions.ValuationOfficeFlaggedForReviewByAdmin, "127.0.0.1",
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FlagOfficeForReviewAsync_BlankReason_ReturnsBadRequest_WithoutTouchingAgency()
    {
        var agencies = new Mock<IAgencyRepository>();
        var service = BuildService(agencies: agencies.Object);

        var result = await service.FlagOfficeForReviewAsync(
            Guid.NewGuid(), "   ", AdminId, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        agencies.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FlagOfficeForReviewAsync_UnknownAgency_ReturnsNotFound()
    {
        var agencies = new Mock<IAgencyRepository>();
        agencies.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((Agency?)null);

        var service = BuildService(agencies: agencies.Object);

        var result = await service.FlagOfficeForReviewAsync(
            Guid.NewGuid(), "سبب", AdminId, null, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.True(result.NotFound);
    }

    [Fact]
    public async Task ClearOfficeReviewFlagAsync_Succeeds_ClearsFlag_AndAuditLogs()
    {
        var agency = Agency.Create("مكتب", "office", Guid.NewGuid(), "SY", DateTime.UtcNow);
        agency.FlagForManualReview("سبب سابق", DateTime.UtcNow);

        var agencies = new Mock<IAgencyRepository>();
        agencies.Setup(x => x.GetByIdAsync(agency.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agency);

        var auditLogs = new Mock<IAuditLogService>();
        var service = BuildService(agencies: agencies.Object, auditLogs: auditLogs.Object);

        var result = await service.ClearOfficeReviewFlagAsync(agency.Id, AdminId, null, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(agency.RequiresManualReview);
        Assert.Null(agency.ManualReviewReason);

        auditLogs.Verify(x => x.LogAsync(
            AdminId, AuditActions.ValuationOfficeReviewFlagClearedByAdmin, null,
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── test scaffolding ────────────────────────────────────────────

    private static AdminValuationInquiryService BuildService(
        IValuationInquiryRepository? inquiries = null,
        IValuationOfficeInvitationRepository? invitations = null,
        IValuationOfficeResponseRepository? responses = null,
        IAgencyRepository? agencies = null,
        IUnitOfWork? unitOfWork = null,
        IAuditLogService? auditLogs = null)
        => new(
            inquiries ?? Mock.Of<IValuationInquiryRepository>(),
            invitations ?? Mock.Of<IValuationOfficeInvitationRepository>(),
            responses ?? Mock.Of<IValuationOfficeResponseRepository>(),
            agencies ?? Mock.Of<IAgencyRepository>(),
            unitOfWork ?? Mock.Of<IUnitOfWork>(),
            auditLogs ?? Mock.Of<IAuditLogService>(),
            NullLogger<AdminValuationInquiryService>.Instance);

    /// <summary>
    /// Agency.Id (declared on BaseEntity) has a protected setter assigned at construction —
    /// tests that need a specific, predictable id (to correlate a mocked repository's rows
    /// with the built entity) set it via reflection, same style AuthTestReflection already
    /// uses elsewhere in this solution for comparable non-public setters.
    /// </summary>
    private static void SetAgencyId(Agency agency, Guid id)
        => typeof(Agency).GetProperty(nameof(Agency.Id))!.SetValue(agency, id);
}
