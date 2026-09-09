using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Queries.GetSocialAccountById;

public sealed record GetSocialAccountByIdQuery(Guid AccountId) : IRequest<SocialAccountDto>;
