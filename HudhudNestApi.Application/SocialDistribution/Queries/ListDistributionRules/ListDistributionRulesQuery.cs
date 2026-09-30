using MediatR;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Queries.ListDistributionRules;

public sealed record ListDistributionRulesQuery(DistributionRuleFilterDto Filter) : IRequest<PagedResult<DistributionRuleDto>>;
