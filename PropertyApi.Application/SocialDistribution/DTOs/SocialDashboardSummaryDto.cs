using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.DTOs;

/// <summary>Phase 10 spec §6 dashboard tiles — built from real SocialPublication rows, never vanity/estimated numbers.</summary>
public sealed record SocialDashboardSummaryDto(
    int PublicationsToday,
    int PublicationsThisWeek,
    int PublicationsThisMonth,
    int Published,
    int Failed,
    int Queued,
    int Retrying,
    int Publishing,
    int Cancelled,
    int DeadLetterUnresolved,
    double SuccessRatePercent,
    double FailureRatePercent,
    IReadOnlyList<PlatformCountDto> ByPlatform,
    IReadOnlyList<GovernorateCountDto> ByProvince);

public sealed record PlatformCountDto(SocialPlatform Platform, int PublishedCount, int FailedCount);

public sealed record GovernorateCountDto(int? GovernorateId, int PublishedCount);
