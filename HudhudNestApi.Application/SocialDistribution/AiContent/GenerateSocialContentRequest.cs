using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.AiContent;

/// <summary>Input to <see cref="ISocialContentGenerator"/> (Phase 8 spec §3). <see cref="Facts"/> is the only source of property data the generator may use.</summary>
public sealed record GenerateSocialContentRequest(
    PropertySocialFacts Facts,
    SocialPlatform Platform,
    string Language,
    string? CampaignCode = null,
    SocialContentTone ContentTone = SocialContentTone.Neutral,
    int? MaxLength = null);
