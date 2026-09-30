using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Queries.GetSocialAccountById;

public sealed record GetSocialAccountByIdQuery(Guid AccountId) : IRequest<SocialAccountDto>;
