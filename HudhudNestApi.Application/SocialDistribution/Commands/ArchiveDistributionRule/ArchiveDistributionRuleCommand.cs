using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Commands.ArchiveDistributionRule;

/// <summary>Terminal logical delete (spec §18: "حذف منطقي أو أرشفة قاعدة") — the DELETE endpoint calls this rather than removing any row.</summary>
public sealed record ArchiveDistributionRuleCommand(Guid RuleId, Guid ArchivedByUserId) : IRequest<DistributionRuleDto>;
