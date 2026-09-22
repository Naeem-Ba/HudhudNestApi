using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Commands.DisconnectSocialAccount;

public sealed record DisconnectSocialAccountCommand(Guid AccountId) : IRequest<SocialAccountDto>;
