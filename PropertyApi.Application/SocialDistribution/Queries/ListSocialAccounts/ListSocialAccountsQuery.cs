using MediatR;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Queries.ListSocialAccounts;

public sealed record ListSocialAccountsQuery(SocialAccountFilterDto Filter) : IRequest<PagedResult<SocialAccountDto>>;
