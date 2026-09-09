using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Commands.ActivateSocialChannel;

public sealed record ActivateSocialChannelCommand(Guid ChannelId) : IRequest<SocialChannelDto>;
