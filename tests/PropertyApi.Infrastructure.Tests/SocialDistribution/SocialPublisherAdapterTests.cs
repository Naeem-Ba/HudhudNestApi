using Microsoft.Extensions.Logging.Abstractions;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;
using PropertyApi.Infrastructure.SocialDistribution.Publishing;

namespace PropertyApi.Infrastructure.Tests.SocialDistribution;

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
        TargetUrl = "https://aqartech.example.com/properties/1",
        Hashtags = hashtags ?? Array.Empty<string>(),
        Language = "ar",
    };

    [Fact]
    public void PublishAsync_AlwaysReturnsPlatformNotConfigured_NeverFabricatesSuccess()
    {
        var publisher = new FacebookPublisher(NullLogger<FacebookPublisher>.Instance);
        var result = publisher.PublishAsync(MakeRequest(SocialPlatform.Facebook)).GetAwaiter().GetResult();

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
