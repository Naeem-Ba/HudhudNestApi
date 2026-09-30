using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Queries.GetDistributionRuleById;

public sealed record GetDistributionRuleByIdQuery(Guid RuleId) : IRequest<DistributionRuleDto>;
