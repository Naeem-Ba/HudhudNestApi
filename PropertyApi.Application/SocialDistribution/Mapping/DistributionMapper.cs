using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Domain.SocialDistribution.Entities;

namespace PropertyApi.Application.SocialDistribution.Mapping;

public static class DistributionMapper
{
    public static DistributionRuleDto ToDto(DistributionRule rule) => new(
        rule.Id,
        rule.Name,
        rule.Description,
        rule.ProvinceId,
        rule.PropertyTypeId,
        rule.TransactionType,
        rule.SocialAccountId,
        rule.Priority,
        rule.IsActive,
        rule.IsArchived,
        rule.StartAt,
        rule.EndAt,
        rule.SpecificityScore,
        rule.CreatedAt,
        rule.UpdatedAt);

    public static DistributionRunDto ToDto(DistributionRun run) => new(
        run.Id,
        run.PropertyId,
        run.TriggerType,
        run.Status,
        run.StartedAt,
        run.CompletedAt,
        run.MatchedRuleCount,
        run.PublicationsCreatedCount,
        run.SkippedCount,
        run.ResultReason,
        run.CreatedAt);
}
