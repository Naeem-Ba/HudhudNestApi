using MediatR;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Queries.ListDistributionRules;

public sealed record ListDistributionRulesQuery(DistributionRuleFilterDto Filter) : IRequest<PagedResult<DistributionRuleDto>>;
