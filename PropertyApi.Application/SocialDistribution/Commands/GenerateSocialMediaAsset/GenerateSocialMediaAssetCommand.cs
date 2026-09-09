using MediatR;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.Commands.GenerateSocialMediaAsset;

/// <summary>Spec §29: "POST /api/social-distribution/assets/generate" — the admin-facing, on-demand equivalent of what the worker does automatically before publishing.</summary>
public sealed record GenerateSocialMediaAssetCommand(
    Guid PropertyId,
    SocialPlatform Platform,
    string Language,
    IReadOnlyList<string> ImageUrls,
    string Title,
    string Body,
    SocialAssetType? AssetType) : IRequest<GeneratedSocialAssetResult>;
