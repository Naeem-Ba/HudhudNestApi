using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Commands.ActivateDistributionRule;

public sealed record ActivateDistributionRuleCommand(Guid RuleId) : IRequest<DistributionRuleDto>;
