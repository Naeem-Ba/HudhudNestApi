using MediatR;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Queries.ListSocialAccounts;

public sealed record ListSocialAccountsQuery(SocialAccountFilterDto Filter) : IRequest<PagedResult<SocialAccountDto>>;
