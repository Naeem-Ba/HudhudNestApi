using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Infrastructure.SocialDistribution.Publishing;

namespace HudhudNestApi.Infrastructure.Tests.SocialDistribution;

/// <summary>
/// Phase 5 spec §31.A: "Publisher Registry يعيد Publisher الصحيح" + "عدم وجود if/else مركزي
/// للمنصات" + "إضافة TikTokPublisher دون تعديل Distribution Engine" — this last one is
/// demonstrated structurally: <see cref="SocialPublisherRegistry"/> takes an
/// <c>IEnumerable&lt;ISocialPublisher&gt;</c> and never references a single concrete platform by
/// name anywhere in its own code (see its source) — adding a 7th platform here is purely a DI
/// registration change, asserted by <see cref="Registry_ResolvesEveryRegisteredPlatform_ByExactType"/>.
/// </summary>
public sealed class SocialPublisherRegistryTests
{
    private static SocialPublisherRegistry BuildRegistry() => new(
    [
        new FacebookPublisher(NullLogger<FacebookPublisher>.Instance),
        new InstagramPublisher(NullLogger<InstagramPublisher>.Instance),
        new TelegramPublisher(NullLogger<TelegramPublisher>.Instance),
        new TikTokPublisher(NullLogger<TikTokPublisher>.Instance),
        new YouTubePublisher(NullLogger<YouTubePublisher>.Instance),
        new LinkedInPublisher(NullLogger<LinkedInPublisher>.Instance),
    ]);

    [Fact]
    public void SupportedPlatforms_ListsAllSixRegisteredPlatforms()
    {
        var registry = BuildRegistry();

        Assert.Equal(6, registry.SupportedPlatforms.Count);
        foreach (var platform in Enum.GetValues<SocialPlatform>())
            Assert.Contains(platform, registry.SupportedPlatforms);
    }

    [Theory]
    [InlineData(SocialPlatform.Facebook, typeof(FacebookPublisher))]
    [InlineData(SocialPlatform.Instagram, typeof(InstagramPublisher))]
    [InlineData(SocialPlatform.Telegram, typeof(TelegramPublisher))]
    [InlineData(SocialPlatform.TikTok, typeof(TikTokPublisher))]
    [InlineData(SocialPlatform.YouTube, typeof(YouTubePublisher))]
    [InlineData(SocialPlatform.LinkedIn, typeof(LinkedInPublisher))]
    public void Registry_ResolvesEveryRegisteredPlatform_ByExactType(SocialPlatform platform, Type expectedType)
    {
        var registry = BuildRegistry();

        Assert.True(registry.HasPublisher(platform));
        var publisher = registry.TryGetPublisher(platform);
        Assert.NotNull(publisher);
        Assert.IsType(expectedType, publisher);
        Assert.Equal(platform, publisher!.Platform);
    }

    [Fact]
    public void Registry_UnregisteredPlatform_ReturnsNullAndHasPublisherFalse()
    {
        // Only register Facebook — simulates a fresh platform (e.g. a real 7th platform) not yet wired up.
        var registry = new SocialPublisherRegistry([new FacebookPublisher(NullLogger<FacebookPublisher>.Instance)]);

        Assert.False(registry.HasPublisher(SocialPlatform.Instagram));
        Assert.Null(registry.TryGetPublisher(SocialPlatform.Instagram));
    }

    [Fact]
    public void Registry_TwoPublishersForSamePlatform_LastRegistrationWins()
    {
        // Simulates swapping a real integration in ahead of the stub for one platform, per
        // PlatformNotConfiguredPublisherBase's own "how to add a real platform" remarks.
        var stub = new FacebookPublisher(NullLogger<FacebookPublisher>.Instance);
        var real = new FacebookPublisher(NullLogger<FacebookPublisher>.Instance);
        var registry = new SocialPublisherRegistry([stub, real]);

        Assert.Same(real, registry.TryGetPublisher(SocialPlatform.Facebook));
    }
}
