using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.Queries.GetPublisherCapabilities;

/// <summary>Spec §29: "GET /api/social-distribution/publishers/{platform}/capabilities".</summary>
public sealed record GetPublisherCapabilitiesQuery(SocialPlatform Platform) : IRequest<SocialPublisherInfoDto>;
