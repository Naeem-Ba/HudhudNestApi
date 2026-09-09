using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.DTOs;

public sealed record DistributionRunDto(
    Guid Id,
    Guid PropertyId,
    DistributionRunTriggerType TriggerType,
    DistributionRunStatus Status,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int MatchedRuleCount,
    int PublicationsCreatedCount,
    int SkippedCount,
    string? ResultReason,
    DateTime CreatedAt);
