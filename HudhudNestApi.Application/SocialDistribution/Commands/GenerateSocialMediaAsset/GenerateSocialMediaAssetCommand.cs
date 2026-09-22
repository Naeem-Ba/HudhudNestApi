using MediatR;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.Commands.GenerateSocialMediaAsset;

/// <summary>Spec §29: "POST /api/social-distribution/assets/generate" — the admin-facing, on-demand equivalent of what the worker does automatically before publishing.</summary>
public sealed record GenerateSocialMediaAssetCommand(
    Guid PropertyId,
    SocialPlatform Platform,
    string Language,
    IReadOnlyList<string> ImageUrls,
    string Title,
    string Body,
    SocialAssetType? AssetType) : IRequest<GeneratedSocialAssetResult>;
