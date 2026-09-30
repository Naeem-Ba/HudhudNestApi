using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.DTOs;

public sealed record SocialChannelDto(
    Guid Id,
    SocialPlatform Platform,
    string Name,
    SocialChannelStatus Status,
    string? ConfigurationVersion,
    DateTime CreatedAt,
    DateTime UpdatedAt);
