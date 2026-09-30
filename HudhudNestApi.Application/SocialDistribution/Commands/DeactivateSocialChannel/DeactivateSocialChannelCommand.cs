using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Commands.DeactivateSocialChannel;

public sealed record DeactivateSocialChannelCommand(Guid ChannelId) : IRequest<SocialChannelDto>;
