using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Domain.SocialDistribution.Attribution;

/// <summary>
/// The one place per-platform UTM defaults for company-operated distribution live (spec §19) —
/// centralized exactly like the frontend's <c>utm-platform-mapping.ts</c> is for Phase 2's
/// visitor-initiated sharing, but intentionally a SEPARATE table of constants: Phase 2's
/// <c>utm_campaign=property_share</c>/<c>utm_content=property_{id}</c> describes a visitor
/// tapping a share button, while this describes AqarTech's own account posting on the
/// property's behalf. Reusing the same campaign name for both would make a report unable to
/// answer "how much of our traffic did WE generate vs. our users" — the spec explicitly asks
/// for this distinction (§19: "اختيار Naming Convention واضح ومختلف").
/// </summary>
public static class SocialDistributionUtmDefaults
{
    public const string UtmMedium = "social";

    public const string UtmCampaign = "social_distribution";

    /// <summary>Phase 13 spec §"مثال UTM للترويج" — a deliberately different campaign name from <see cref="UtmCampaign"/>, so a report can separate "routine auto-distribution" traffic from "a deliberate repost/promotion" traffic.</summary>
    public const string UtmCampaignPromotion = "property_promotion";

    public static string UtmContentForPublication(Guid publicationId) => $"publication_{publicationId}";

    /// <summary>Phase 13 spec §"مثال UTM للترويج": <c>utm_content=promotion_{'{'}public-id{'}'}</c>.</summary>
    public static string UtmContentForPromotion(Guid propertyId) => $"promotion_{propertyId}";

    /// <summary>Lowercase, UTM-safe token per platform — matches this codebase's own UTM charset (letters/digits/._-, spec §4).</summary>
    public static string UtmSourceFor(SocialPlatform platform) => platform switch
    {
        SocialPlatform.Facebook => "facebook",
        SocialPlatform.Instagram => "instagram",
        SocialPlatform.Telegram => "telegram",
        SocialPlatform.TikTok => "tiktok",
        SocialPlatform.YouTube => "youtube",
        SocialPlatform.LinkedIn => "linkedin",
        _ => "other",
    };
}
