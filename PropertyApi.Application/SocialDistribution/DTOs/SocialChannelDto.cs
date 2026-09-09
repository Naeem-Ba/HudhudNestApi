using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.DTOs;

public sealed record SocialChannelDto(
    Guid Id,
    SocialPlatform Platform,
    string Name,
    SocialChannelStatus Status,
    string? ConfigurationVersion,
    DateTime CreatedAt,
    DateTime UpdatedAt);
