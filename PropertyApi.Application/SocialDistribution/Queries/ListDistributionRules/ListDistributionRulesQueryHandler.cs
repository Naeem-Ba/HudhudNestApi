using MediatR;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;

namespace PropertyApi.Application.SocialDistribution.Queries.ListDistributionRules;

public sealed class ListDistributionRulesQueryHandler : IRequestHandler<ListDistributionRulesQuery, PagedResult<DistributionRuleDto>>
{
    private readonly IDistributionRuleRepository _rules;

    public ListDistributionRulesQueryHandler(IDistributionRuleRepository rules) => _rules = rules;

    public async Task<PagedResult<DistributionRuleDto>> Handle(ListDistributionRulesQuery request, CancellationToken ct)
    {
        var page = await _rules.GetPagedAsync(request.Filter, ct);

        return new PagedResult<DistributionRuleDto>
        {
            Items = page.Items.Select(DistributionMapper.ToDto).ToList(),
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize,
        };
    }
}
