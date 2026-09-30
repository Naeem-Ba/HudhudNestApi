using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.DTOs;

/// <summary>
/// Input to <see cref="Services.ISocialMediaAssetGenerator"/> (Phase 7 spec §17). Deliberately
/// carries only identifiers/hints — the generator itself loads the property's own price/area/
/// rooms/location fields via <c>IPropertyRepository</c> rather than requiring every caller to
/// re-fetch and pass them, so <c>PublishSocialPublicationCommandHandler</c> and the
/// <c>POST /assets/generate</c> admin endpoint share one code path with no duplicated field
/// wiring.
/// </summary>
public sealed record GenerateSocialAssetRequest(
    Guid PropertyId,
    SocialPlatform Platform,
    string TemplateId,
    string Language,
    IReadOnlyList<string> ImageUrls,
    string Title,
    string Body,
    SocialAssetType? AssetType = null);
