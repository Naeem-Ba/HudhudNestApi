using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Commands.DeactivateDistributionRule;

public sealed record DeactivateDistributionRuleCommand(Guid RuleId) : IRequest<DistributionRuleDto>;
