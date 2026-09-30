using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Models;
using HudhudNestApi.Infrastructure.SocialDistribution.Publishing;

namespace HudhudNestApi.Infrastructure.Tests.SocialDistribution;

/// <summary>
/// Phase 5 spec §31.B (Publisher Contract Tests). Every adapter here is still a placeholder for
/// the actual network call (see <see cref="PlatformNotConfiguredPublisherBase"/>), but
/// <see cref="ISocialPublisher.ValidateContent"/> is real, platform-aware logic — these tests hold
/// it to that.
/// </summary>
public sealed class SocialPublisherAdapterTests
{
    private static SocialPublishRequest MakeRequest(
        SocialPlatform platform, string? imageUrl = "https://cdn.example.com/img.jpg", string body = "نص قصير", IReadOnlyList<string>? hashtags = null) => new()
        {
            PublicationId = Guid.NewGuid(),
            Platform = platform,
            ExternalAccountId = "ext-1",
            CredentialReference = null,
            Title = "عنوان",
            Body = body,
            ImageUrl = imageUrl ?? string.Empty,
            TargetUrl = "https://hudhudnest.example.com/properties/1",
            Hashtags = hashtags ?? Array.Empty<string>(),
            Language = "ar",
        };

    [Fact]
    public async Task PublishAsync_AlwaysReturnsPlatformNotConfigured_NeverFabricatesSuccess()
    {
        var publisher = new FacebookPublisher(NullLogger<FacebookPublisher>.Instance);
        var result = await publisher.PublishAsync(MakeRequest(SocialPlatform.Facebook));

        Assert.False(result.IsSuccess);
        Assert.Equal(SocialPublicationErrorCode.PlatformNotConfigured, result.ErrorCode);
        Assert.Null(result.ExternalPostId);
    }

    [Fact]
    public void Instagram_RequiresImage_ValidationFailsWithoutOne()
    {
        var publisher = new InstagramPublisher(NullLogger<InstagramPublisher>.Instance);
        var result = publisher.ValidateContent(MakeRequest(SocialPlatform.Instagram, imageUrl: null));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("صورة"));
    }

    [Fact]
    public void Instagram_WithImage_ValidationSucceeds()
    {
        var publisher = new InstagramPublisher(NullLogger<InstagramPublisher>.Instance);
        var result = publisher.ValidateContent(MakeRequest(SocialPlatform.Instagram));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Facebook_DoesNotRequireImage_ValidationSucceedsWithoutOne()
    {
        var publisher = new FacebookPublisher(NullLogger<FacebookPublisher>.Instance);
        var result = publisher.ValidateContent(MakeRequest(SocialPlatform.Facebook, imageUrl: null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateContent_BodyExceedsPlatformLimit_Fails()
    {
        var publisher = new TelegramPublisher(NullLogger<TelegramPublisher>.Instance);
        var tooLong = new string('a', 5000); // Telegram's real limit (SocialContentPolicy) is 4096
        var result = publisher.ValidateContent(MakeRequest(SocialPlatform.Telegram, body: tooLong));

        Assert.False(result.IsValid);
    }

    // Phase 1 audit F-9: Telegram's real sendPhoto caption limit (1024) is stricter than
    // sendMessage's 4096 (SocialContentPolicy.MaxBodyLength for this platform) — every automatic
    // publication attaches an image, so a body that fits the generic limit but not the
    // photo-caption one must still fail local validation instead of being rejected by Telegram
    // itself after this system already believed the content was valid.
    [Fact]
    public void Telegram_WithImage_BodyOverCaptionLimitButUnderGenericBodyLimit_Fails()
    {
        var publisher = new TelegramPublisher(NullLogger<TelegramPublisher>.Instance);
        var body = new string('a', 1500); // over the 1024 caption limit, well under the 4096 body limit
        var result = publisher.ValidateContent(MakeRequest(SocialPlatform.Telegram, body: body));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("صورة"));
    }

    [Fact]
    public void Telegram_WithoutImage_BodyOverCaptionLimitButUnderGenericBodyLimit_Succeeds()
    {
        // sendMessage (no photo) is not subject to the caption ceiling at all.
        var publisher = new TelegramPublisher(NullLogger<TelegramPublisher>.Instance);
        var body = new string('a', 1500);
        var result = publisher.ValidateContent(MakeRequest(SocialPlatform.Telegram, imageUrl: null, body: body));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Telegram_WithImage_BodyWithinCaptionLimit_Succeeds()
    {
        var publisher = new TelegramPublisher(NullLogger<TelegramPublisher>.Instance);
        var body = new string('a', 1024);
        var result = publisher.ValidateContent(MakeRequest(SocialPlatform.Telegram, body: body));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidateContent_InvalidImageUrl_Fails()
    {
        var publisher = new FacebookPublisher(NullLogger<FacebookPublisher>.Instance);
        var result = publisher.ValidateContent(MakeRequest(SocialPlatform.Facebook, imageUrl: "javascript:alert(1)"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ValidateContent_InvalidTargetUrl_Fails()
    {
        var publisher = new FacebookPublisher(NullLogger<FacebookPublisher>.Instance);
        var request = new SocialPublishRequest
        {
            PublicationId = Guid.NewGuid(),
            Platform = SocialPlatform.Facebook,
            ExternalAccountId = "ext-1",
            Title = "عنوان",
            Body = "نص",
            ImageUrl = "https://cdn.example.com/img.jpg",
            TargetUrl = "/internal/route",
            Hashtags = Array.Empty<string>(),
            Language = "ar",
        };

        Assert.False(publisher.ValidateContent(request).IsValid);
    }

    [Fact]
    public void TikTok_NoImageSupport_DoesNotRequireOne_ButCapabilitiesReflectNoImages()
    {
        var publisher = new TikTokPublisher(NullLogger<TikTokPublisher>.Instance);
        var capabilities = publisher.GetCapabilities();

        Assert.False(capabilities.SupportsImages);
        Assert.False(capabilities.SupportsUpdate);
        Assert.False(capabilities.SupportsDelete);
    }

    [Theory]
    [InlineData(SocialPlatform.Facebook)]
    [InlineData(SocialPlatform.Instagram)]
    [InlineData(SocialPlatform.Telegram)]
    [InlineData(SocialPlatform.TikTok)]
    [InlineData(SocialPlatform.YouTube)]
    [InlineData(SocialPlatform.LinkedIn)]
    public void EveryAdapter_ReportsItsOwnPlatform_MatchingItsClassName(SocialPlatform platform)
    {
        ISocialPublisher publisher = platform switch
        {
            SocialPlatform.Facebook => new FacebookPublisher(NullLogger<FacebookPublisher>.Instance),
            SocialPlatform.Instagram => new InstagramPublisher(NullLogger<InstagramPublisher>.Instance),
            SocialPlatform.Telegram => new TelegramPublisher(NullLogger<TelegramPublisher>.Instance),
            SocialPlatform.TikTok => new TikTokPublisher(NullLogger<TikTokPublisher>.Instance),
            SocialPlatform.YouTube => new YouTubePublisher(NullLogger<YouTubePublisher>.Instance),
            SocialPlatform.LinkedIn => new LinkedInPublisher(NullLogger<LinkedInPublisher>.Instance),
            _ => throw new NotSupportedException(),
        };

        Assert.Equal(platform, publisher.Platform);
    }
}
