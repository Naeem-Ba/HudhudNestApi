using Microsoft.Extensions.Logging;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;

namespace PropertyApi.Infrastructure.SocialDistribution.Publishing;

/// <summary>
/// TikTok adapter. Placeholder — see <see cref="PlatformNotConfiguredPublisherBase"/>. Video-first
/// platform: <see cref="ISocialPublisher.GetCapabilities"/> reports no update/delete support,
/// matching TikTok's real Content Posting API, which does not expose either operation.
/// </summary>
public sealed class TikTokPublisher : PlatformNotConfiguredPublisherBase
{
    public override SocialPlatform Platform => SocialPlatform.TikTok;

    public TikTokPublisher(ILogger<TikTokPublisher> logger) : base(logger) { }

    public override SocialPublisherCapabilities GetCapabilities() => new(
        SupportsText: true,
        SupportsImages: false,
        SupportsVideo: true,
        SupportsStories: false,
        SupportsHashtags: true,
        SupportsScheduling: false,
        SupportsUpdate: false,
        SupportsDelete: false,
        MaxTextLength: 100,
        MaxImages: 0,
        RequiresImage: false);
}
