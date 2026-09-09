using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.DTOs;

public sealed record SocialPublicationFilterDto(
    Guid? PropertyId,
    Guid? SocialAccountId,
    SocialPlatform? Platform,
    SocialPublicationStatus? Status,
    DateTime? FromDate,
    DateTime? ToDate,
    int Page = 1,
    int PageSize = 20,
    /// <summary>Phase 10 spec §6: filter Publications Management by the DistributionRule that created them — null matches manually-created publications too.</summary>
    Guid? DistributionRuleId = null,
    /// <summary>Phase 10 spec §6: filter by the target account's province.</summary>
    int? GovernorateId = null);
