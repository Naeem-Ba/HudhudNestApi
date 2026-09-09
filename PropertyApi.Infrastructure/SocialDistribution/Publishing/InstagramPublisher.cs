using Microsoft.Extensions.Logging;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;

namespace PropertyApi.Infrastructure.SocialDistribution.Publishing;

/// <summary>
/// Instagram Business Account adapter. Placeholder — see <see cref="PlatformNotConfiguredPublisherBase"/>.
/// A real implementation would use the Instagram Graph API's container-then-publish flow, which
/// requires a publicly reachable image URL — reflected here via <see cref="RequiresImage"/>-style
/// capability so <see cref="ValidateContent"/> rejects a text-only request before ever creating a
/// job for it.
/// </summary>
public sealed class InstagramPublisher : PlatformNotConfiguredPublisherBase
{
    public override SocialPlatform Platform => SocialPlatform.Instagram;

    public InstagramPublisher(ILogger<InstagramPublisher> logger) : base(logger) { }

    public override SocialPublisherCapabilities GetCapabilities() => new(
        SupportsText: true,
        SupportsImages: true,
        SupportsVideo: true,
        SupportsStories: true,
        SupportsHashtags: true,
        SupportsScheduling: false,
        SupportsUpdate: false,
        SupportsDelete: true,
        MaxTextLength: 100,
        MaxImages: 10,
        RequiresImage: true);
}
