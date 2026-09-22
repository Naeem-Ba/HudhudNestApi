using MediatR;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;

namespace HudhudNestApi.Application.SocialDistribution.Queries.ListSocialAccounts;

public sealed class ListSocialAccountsQueryHandler : IRequestHandler<ListSocialAccountsQuery, PagedResult<SocialAccountDto>>
{
    private readonly ISocialAccountRepository _accounts;

    public ListSocialAccountsQueryHandler(ISocialAccountRepository accounts) => _accounts = accounts;

    public async Task<PagedResult<SocialAccountDto>> Handle(ListSocialAccountsQuery request, CancellationToken ct)
    {
        var page = await _accounts.GetPagedAsync(request.Filter, ct);

        return new PagedResult<SocialAccountDto>
        {
            Items = page.Items.Select(SocialDistributionMapper.ToDto).ToList(),
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize,
        };
    }
}
