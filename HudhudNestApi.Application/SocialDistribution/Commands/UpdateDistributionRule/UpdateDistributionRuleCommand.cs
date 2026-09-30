using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.SocialDistribution.Commands.UpdateDistributionRule;

/// <summary>Updates matching criteria/priority/validity window. Does not change the target SocialAccountId or the Active/Archived flags — see ActivateDistributionRule/DeactivateDistributionRule/ArchiveDistributionRule for those.</summary>
public sealed record UpdateDistributionRuleCommand(
    Guid RuleId,
    string Name,
    string? Description,
    int? ProvinceId,
    int? PropertyTypeId,
    ListingType? TransactionType,
    int Priority,
    DateTime? StartAt,
    DateTime? EndAt,
    Guid UpdatedByUserId) : IRequest<DistributionRuleDto>;
