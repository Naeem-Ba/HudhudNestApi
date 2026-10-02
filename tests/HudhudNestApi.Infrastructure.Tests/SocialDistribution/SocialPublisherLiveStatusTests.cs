using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Infrastructure.SocialDistribution;
using HudhudNestApi.Infrastructure.SocialDistribution.Publishing;

namespace HudhudNestApi.Infrastructure.Tests.SocialDistribution;

/// <summary>
/// The admin UI must not offer to publish on a platform that can only ever answer
/// <c>PlatformNotConfigured</c>. "Live" is decided by what the registry actually resolves for a
/// platform: a placeholder is never live; a real adapter is live exactly when its token was
/// configured (the registration only adds it then).
/// </summary>
public sealed class SocialPublisherLiveStatusTests
{
    private static ISocialPublisherRegistry BuildRegistry(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSocialDistributionInfrastructure(configuration);
        return services.BuildServiceProvider().GetRequiredService<ISocialPublisherRegistry>();
    }

    [Fact]
    public void WithNoTokensConfigured_NoPlatformIsLive()
    {
        var registry = BuildRegistry([]);

        foreach (var platform in registry.SupportedPlatforms)
            Assert.False(registry.TryGetPublisher(platform)!.IsLive, $"{platform} must not be live without a credential");
    }

    [Fact]
    public void ConfiguringATelegramToken_MakesOnlyTelegramLive()
    {
        var registry = BuildRegistry(new() { [$"{TelegramBotOptions.SectionName}:BotToken"] = "123456:fake-token" });

        Assert.True(registry.TryGetPublisher(SocialPlatform.Telegram)!.IsLive);
        Assert.False(registry.TryGetPublisher(SocialPlatform.Facebook)!.IsLive);
        Assert.False(registry.TryGetPublisher(SocialPlatform.Instagram)!.IsLive);
        Assert.False(registry.TryGetPublisher(SocialPlatform.TikTok)!.IsLive);
    }

    [Fact]
    public void ConfiguringAllThreeRealCredentials_MakesThoseThreePlatformsLive_AndNeverTheUnimplementedOnes()
    {
        var registry = BuildRegistry(new()
        {
            [$"{TelegramBotOptions.SectionName}:BotToken"] = "123456:fake-token",
            [$"{FacebookGraphApiOptions.SectionName}:PageAccessToken"] = "fake-page-token",
            [$"{InstagramGraphApiOptions.SectionName}:AccessToken"] = "fake-ig-token",
        });

        Assert.All(
            new[] { SocialPlatform.Telegram, SocialPlatform.Facebook, SocialPlatform.Instagram },
            platform => Assert.True(registry.TryGetPublisher(platform)!.IsLive, $"{platform} should be live"));
        Assert.All(
            new[] { SocialPlatform.TikTok, SocialPlatform.YouTube, SocialPlatform.LinkedIn },
            platform => Assert.False(registry.TryGetPublisher(platform)!.IsLive, $"{platform} has no real adapter"));
    }

    [Fact]
    public void EveryPlaceholderAdapter_ReportsNotLive_Directly()
    {
        ISocialPublisher[] placeholders =
        [
            new FacebookPublisher(NullLogger<FacebookPublisher>.Instance),
            new InstagramPublisher(NullLogger<InstagramPublisher>.Instance),
            new TelegramPublisher(NullLogger<TelegramPublisher>.Instance),
            new TikTokPublisher(NullLogger<TikTokPublisher>.Instance),
            new YouTubePublisher(NullLogger<YouTubePublisher>.Instance),
            new LinkedInPublisher(NullLogger<LinkedInPublisher>.Instance),
        ];

        Assert.All(placeholders, placeholder => Assert.False(placeholder.IsLive));
    }
}
