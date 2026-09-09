using MediatR;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;

namespace PropertyApi.Application.SocialDistribution.Queries.ListSocialAccounts;

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
