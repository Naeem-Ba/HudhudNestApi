using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.DTOs;

public sealed record SocialPublicationDto(
    Guid Id,
    Guid PropertyId,
    Guid SocialAccountId,
    SocialPublicationStatus Status,
    DateTime? ScheduledAt,
    DateTime? StartedAt,
    DateTime? PublishedAt,
    DateTime? FailedAt,
    DateTime? CancelledAt,
    string? ExternalPostId,
    string? ExternalPostUrl,
    SocialPublicationErrorCode? ErrorCode,
    string? ErrorMessage,
    int RetryCount,
    int MaxRetryCount,
    DateTime? LastRetryAt,
    DateTime? NextRetryAt,
    string UtmSource,
    string UtmMedium,
    string UtmCampaign,
    string UtmContent,
    SocialPostContentDto? Content,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    Guid? DistributionRuleId = null,
    Guid? DistributionRunId = null);
