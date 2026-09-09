using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;

namespace PropertyApi.Application.SocialDistribution.DTOs;

public sealed record SocialPublisherInfoDto(SocialPlatform Platform, SocialPublisherCapabilities Capabilities);
