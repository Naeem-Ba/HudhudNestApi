using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.Commands.CreateSocialChannel;

public sealed record CreateSocialChannelCommand(
    SocialPlatform Platform,
    string Name,
    string? ConfigurationVersion) : IRequest<SocialChannelDto>;
