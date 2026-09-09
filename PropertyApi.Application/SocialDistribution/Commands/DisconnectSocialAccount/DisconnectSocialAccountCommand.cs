using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Commands.DisconnectSocialAccount;

public sealed record DisconnectSocialAccountCommand(Guid AccountId) : IRequest<SocialAccountDto>;
