using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.Queries.GetPublisherCapabilities;

/// <summary>Spec §29: "GET /api/social-distribution/publishers/{platform}/capabilities".</summary>
public sealed record GetPublisherCapabilitiesQuery(SocialPlatform Platform) : IRequest<SocialPublisherInfoDto>;
