using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Commands.DeactivateSocialChannel;

public sealed record DeactivateSocialChannelCommand(Guid ChannelId) : IRequest<SocialChannelDto>;
