using MediatR;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;

namespace HudhudNestApi.Application.SocialDistribution.Queries.ListDistributionRules;

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
