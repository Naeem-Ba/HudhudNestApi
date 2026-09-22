using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Commands.ActivateDistributionRule;

public sealed record ActivateDistributionRuleCommand(Guid RuleId) : IRequest<DistributionRuleDto>;
