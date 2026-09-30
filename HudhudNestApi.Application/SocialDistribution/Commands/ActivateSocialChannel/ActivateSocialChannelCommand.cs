using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Commands.ActivateSocialChannel;

public sealed record ActivateSocialChannelCommand(Guid ChannelId) : IRequest<SocialChannelDto>;
