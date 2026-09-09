using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.Commands.CreateSocialChannel;

public sealed record CreateSocialChannelCommand(
    SocialPlatform Platform,
    string Name,
    string? ConfigurationVersion) : IRequest<SocialChannelDto>;
