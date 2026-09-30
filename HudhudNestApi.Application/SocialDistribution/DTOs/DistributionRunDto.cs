using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.DTOs;

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
