using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Domain.SocialDistribution.Policies;

/// <summary>
/// Centralized, pure dimension/format table for every (Platform, AssetType) combination the
/// template engine can produce (Phase 7 spec §18: "لا تثبت الأبعاد داخل عدة ملفات"). Exactly one
/// place to add or adjust a preset — <see cref="Templates.SocialAssetTemplateRenderer"/> and
/// <see cref="Application.SocialDistribution.Services.SocialMediaAssetGenerator"/> both read from
/// here, never duplicate a width/height literal of their own.
/// </summary>
public static class SocialAssetPresetCatalog
{
    public sealed record Preset(int Width, int Height, string Format, int Quality, int MaxFileSizeBytes);

    /// <summary>
    /// Real-world-informed presets. Deliberately conservative MaxFileSizeBytes (well under every
    /// platform's actual upload ceiling) so a validation failure here is a signal, not routine.
    /// </summary>
    private static readonly Dictionary<(SocialPlatform Platform, SocialAssetType AssetType), Preset> Presets = new()
    {
        [(SocialPlatform.Facebook, SocialAssetType.FeedImage)] = new Preset(1200, 630, "png", 90, 5 * 1024 * 1024),
        [(SocialPlatform.Facebook, SocialAssetType.StoryImage)] = new Preset(1080, 1920, "png", 90, 5 * 1024 * 1024),
        [(SocialPlatform.Instagram, SocialAssetType.SquareImage)] = new Preset(1080, 1080, "png", 90, 8 * 1024 * 1024),
        [(SocialPlatform.Instagram, SocialAssetType.PortraitImage)] = new Preset(1080, 1350, "png", 90, 8 * 1024 * 1024),
        [(SocialPlatform.Instagram, SocialAssetType.StoryImage)] = new Preset(1080, 1920, "png", 90, 8 * 1024 * 1024),
        [(SocialPlatform.Telegram, SocialAssetType.TelegramImage)] = new Preset(1280, 720, "png", 85, 5 * 1024 * 1024),
        [(SocialPlatform.LinkedIn, SocialAssetType.FeedImage)] = new Preset(1200, 627, "png", 90, 5 * 1024 * 1024),
        [(SocialPlatform.TikTok, SocialAssetType.StoryImage)] = new Preset(1080, 1920, "png", 90, 8 * 1024 * 1024),
        [(SocialPlatform.YouTube, SocialAssetType.FeedImage)] = new Preset(1280, 720, "png", 90, 5 * 1024 * 1024),
    };

    /// <summary>The AssetType a platform gets when the caller does not pin one explicitly — its most natural default composition.</summary>
    private static readonly Dictionary<SocialPlatform, SocialAssetType> DefaultAssetTypeByPlatform = new()
    {
        [SocialPlatform.Facebook] = SocialAssetType.FeedImage,
        [SocialPlatform.Instagram] = SocialAssetType.SquareImage,
        [SocialPlatform.Telegram] = SocialAssetType.TelegramImage,
        [SocialPlatform.LinkedIn] = SocialAssetType.FeedImage,
        [SocialPlatform.TikTok] = SocialAssetType.StoryImage,
        [SocialPlatform.YouTube] = SocialAssetType.FeedImage,
    };

    public static SocialAssetType GetDefaultAssetType(SocialPlatform platform) =>
        DefaultAssetTypeByPlatform.TryGetValue(platform, out var assetType) ? assetType : SocialAssetType.FeedImage;

    /// <summary>Null when this exact (platform, assetType) pair has no defined preset — the caller must not guess dimensions.</summary>
    public static Preset? TryGetPreset(SocialPlatform platform, SocialAssetType assetType) =>
        Presets.TryGetValue((platform, assetType), out var preset) ? preset : null;
}
