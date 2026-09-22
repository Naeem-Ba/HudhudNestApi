using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.SocialDistribution.Commands.CreateDistributionRule;

/// <summary>
/// Creates a new, immediately-active-by-default <see cref="Domain.SocialDistribution.Entities.DistributionRule"/>
/// (Phase 4 spec §5/§18). Null ProvinceId/PropertyTypeId/TransactionType each mean "matches every
/// value of this dimension" — see the entity's field docs.
/// </summary>
public sealed record CreateDistributionRuleCommand(
    string Name,
    string? Description,
    int? ProvinceId,
    int? PropertyTypeId,
    ListingType? TransactionType,
    Guid SocialAccountId,
    int Priority,
    DateTime? StartAt,
    DateTime? EndAt,
    Guid CreatedByUserId) : IRequest<DistributionRuleDto>;
