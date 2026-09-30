using Moq;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Queries.GetSocialDashboardSummary;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

/// <summary>Phase 10 spec §6 — the dashboard handler stitches the publications aggregate together with the dead-letter count, never fabricating either.</summary>
public sealed class GetSocialDashboardSummaryQueryHandlerTests
{
    [Fact]
    public async Task Handle_CombinesPublicationSummary_WithUnresolvedDeadLetterCount()
    {
        var publications = new Mock<ISocialPublicationRepository>();
        var deadLetters = new Mock<ISocialPublicationDeadLetterRepository>();

        var baseSummary = new SocialDashboardSummaryDto(
            PublicationsToday: 5, PublicationsThisWeek: 20, PublicationsThisMonth: 80,
            Published: 4, Failed: 1, Queued: 2, Retrying: 1, Publishing: 0, Cancelled: 0,
            DeadLetterUnresolved: 0, SuccessRatePercent: 80, FailureRatePercent: 20,
            ByPlatform: Array.Empty<PlatformCountDto>(), ByProvince: Array.Empty<GovernorateCountDto>());

        publications.Setup(x => x.GetDashboardSummaryAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(baseSummary);

        deadLetters.Setup(x => x.GetPagedAsync(false, 1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<SocialPublicationDeadLetter>
            {
                Items = Array.Empty<SocialPublicationDeadLetter>(),
                TotalCount = 3,
                Page = 1,
                PageSize = 1,
            });

        var handler = new GetSocialDashboardSummaryQueryHandler(publications.Object, deadLetters.Object);
        var result = await handler.Handle(new GetSocialDashboardSummaryQuery(), CancellationToken.None);

        Assert.Equal(5, result.PublicationsToday);
        Assert.Equal(3, result.DeadLetterUnresolved);
        Assert.Equal(80, result.SuccessRatePercent);
    }
}
