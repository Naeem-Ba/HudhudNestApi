using Microsoft.Extensions.Logging;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;

namespace PropertyApi.Infrastructure.SocialDistribution.Publishing;

/// <summary>YouTube (Community post / video description) adapter. Placeholder — see <see cref="PlatformNotConfiguredPublisherBase"/>.</summary>
public sealed class YouTubePublisher : PlatformNotConfiguredPublisherBase
{
    public override SocialPlatform Platform => SocialPlatform.YouTube;

    public YouTubePublisher(ILogger<YouTubePublisher> logger) : base(logger) { }

    public override SocialPublisherCapabilities GetCapabilities() => new(
        SupportsText: true,
        SupportsImages: true,
        SupportsVideo: true,
        SupportsStories: false,
        SupportsHashtags: true,
        SupportsScheduling: true,
        SupportsUpdate: true,
        SupportsDelete: true,
        MaxTextLength: 100,
        MaxImages: 1,
        RequiresImage: false);
}
