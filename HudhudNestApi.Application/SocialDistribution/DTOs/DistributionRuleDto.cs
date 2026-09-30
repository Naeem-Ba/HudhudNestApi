using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.SocialDistribution.DTOs;

public sealed record DistributionRuleDto(
    Guid Id,
    string Name,
    string? Description,
    int? ProvinceId,
    int? PropertyTypeId,
    ListingType? TransactionType,
    Guid SocialAccountId,
    int Priority,
    bool IsActive,
    bool IsArchived,
    DateTime? StartAt,
    DateTime? EndAt,
    int SpecificityScore,
    DateTime CreatedAt,
    DateTime UpdatedAt);
