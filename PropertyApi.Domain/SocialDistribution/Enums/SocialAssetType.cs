namespace PropertyApi.Domain.SocialDistribution.Enums;

/// <summary>The shape/purpose of a generated <see cref="Entities.SocialMediaAsset"/> (Phase 7 spec §18).</summary>
public enum SocialAssetType
{
    /// <summary>Standard feed post image (e.g. Facebook feed, 1200x630-class landscape).</summary>
    FeedImage = 1,

    /// <summary>1:1 square (e.g. Instagram feed post).</summary>
    SquareImage = 2,

    /// <summary>4:5 portrait (e.g. Instagram portrait feed post).</summary>
    PortraitImage = 3,

    /// <summary>9:16 full-screen vertical (Facebook/Instagram Stories).</summary>
    StoryImage = 4,

    /// <summary>Telegram's preferred attached-photo aspect ratio.</summary>
    TelegramImage = 5,

    /// <summary>Reserved for a future video-thumbnail asset type — not produced by the template engine today.</summary>
    VideoThumbnail = 6,
}
