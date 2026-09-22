using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Models;

namespace HudhudNestApi.Application.SocialDistribution.DTOs;

public sealed record SocialPublisherInfoDto(SocialPlatform Platform, SocialPublisherCapabilities Capabilities);
