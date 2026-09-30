using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Policies;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class SocialAssetPresetCatalogTests
{
    [Theory]
    [InlineData(SocialPlatform.Facebook, SocialAssetType.FeedImage, 1200, 630)]
    [InlineData(SocialPlatform.Instagram, SocialAssetType.SquareImage, 1080, 1080)]
    [InlineData(SocialPlatform.Instagram, SocialAssetType.StoryImage, 1080, 1920)]
    [InlineData(SocialPlatform.Telegram, SocialAssetType.TelegramImage, 1280, 720)]
    public void TryGetPreset_KnownCombination_ReturnsExpectedDimensions(SocialPlatform platform, SocialAssetType assetType, int width, int height)
    {
        var preset = SocialAssetPresetCatalog.TryGetPreset(platform, assetType);

        Assert.NotNull(preset);
        Assert.Equal(width, preset!.Width);
        Assert.Equal(height, preset.Height);
    }

    [Fact]
    public void TryGetPreset_UnknownCombination_ReturnsNull_NeverGuessesDimensions() =>
        Assert.Null(SocialAssetPresetCatalog.TryGetPreset(SocialPlatform.Telegram, SocialAssetType.PortraitImage));

    [Theory]
    [InlineData(SocialPlatform.Facebook, SocialAssetType.FeedImage)]
    [InlineData(SocialPlatform.Instagram, SocialAssetType.SquareImage)]
    [InlineData(SocialPlatform.Telegram, SocialAssetType.TelegramImage)]
    [InlineData(SocialPlatform.TikTok, SocialAssetType.StoryImage)]
    [InlineData(SocialPlatform.YouTube, SocialAssetType.FeedImage)]
    [InlineData(SocialPlatform.LinkedIn, SocialAssetType.FeedImage)]
    public void GetDefaultAssetType_EveryPlatform_HasASensibleDefault_WithADefinedPreset(SocialPlatform platform, SocialAssetType expected)
    {
        var defaultType = SocialAssetPresetCatalog.GetDefaultAssetType(platform);
        Assert.Equal(expected, defaultType);
        Assert.NotNull(SocialAssetPresetCatalog.TryGetPreset(platform, defaultType));
    }
}
