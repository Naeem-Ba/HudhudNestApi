using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Models;

namespace HudhudNestApi.Application.SocialDistribution.DTOs;

/// <param name="IsLive">False when the registered adapter is the placeholder (no credential configured, or no real integration exists) — posting through it always fails with PlatformNotConfigured.</param>
public sealed record SocialPublisherInfoDto(SocialPlatform Platform, SocialPublisherCapabilities Capabilities, bool IsLive);
