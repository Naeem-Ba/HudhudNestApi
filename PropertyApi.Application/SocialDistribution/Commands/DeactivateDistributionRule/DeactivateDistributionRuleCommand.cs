using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Commands.DeactivateDistributionRule;

public sealed record DeactivateDistributionRuleCommand(Guid RuleId) : IRequest<DistributionRuleDto>;
