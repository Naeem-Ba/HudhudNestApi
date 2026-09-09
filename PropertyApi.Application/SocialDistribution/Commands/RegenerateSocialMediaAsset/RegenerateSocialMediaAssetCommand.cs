using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Commands.RegenerateSocialMediaAsset;

/// <summary>
/// Spec §29: "POST /api/social-distribution/assets/{assetId}/regenerate" — forces a fresh render
/// even if a byte-identical asset already exists for this property/platform/template combination
/// (bypasses <c>ISocialMediaAssetGenerator</c>'s normal checksum-reuse — spec §23: "ولّد Asset
/// جديداً فقط عند تغيّر ..."; this endpoint is the explicit override for when an operator wants a
/// regeneration anyway, e.g. after fixing a source photo).
/// </summary>
public sealed record RegenerateSocialMediaAssetCommand(Guid AssetId) : IRequest<GeneratedSocialAssetResult>;
