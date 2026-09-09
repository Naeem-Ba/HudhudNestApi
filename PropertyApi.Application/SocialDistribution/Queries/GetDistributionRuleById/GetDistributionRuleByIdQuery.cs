using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Queries.GetDistributionRuleById;

public sealed record GetDistributionRuleByIdQuery(Guid RuleId) : IRequest<DistributionRuleDto>;
