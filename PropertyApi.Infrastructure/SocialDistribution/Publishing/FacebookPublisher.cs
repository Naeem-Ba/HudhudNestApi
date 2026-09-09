using Microsoft.Extensions.Logging;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;

namespace PropertyApi.Infrastructure.SocialDistribution.Publishing;

/// <summary>
/// Facebook Page adapter. Placeholder — see <see cref="PlatformNotConfiguredPublisherBase"/> for
/// what is real (content validation against real capabilities) vs. Mock (the publish call
/// itself). A real implementation would call the official Facebook Graph API's Page feed/photo
/// endpoints here, behind a token-refresh-aware HTTP client — nothing else in this codebase would
/// need to change.
/// </summary>
public sealed class FacebookPublisher : PlatformNotConfiguredPublisherBase
{
    public override SocialPlatform Platform => SocialPlatform.Facebook;

    public FacebookPublisher(ILogger<FacebookPublisher> logger) : base(logger) { }

    public override SocialPublisherCapabilities GetCapabilities() => new(
        SupportsText: true,
        SupportsImages: true,
        SupportsVideo: true,
        SupportsStories: true,
        SupportsHashtags: true,
        SupportsScheduling: true,
        SupportsUpdate: true,
        SupportsDelete: true,
        MaxTextLength: 100,
        MaxImages: 10,
        RequiresImage: false);
}
