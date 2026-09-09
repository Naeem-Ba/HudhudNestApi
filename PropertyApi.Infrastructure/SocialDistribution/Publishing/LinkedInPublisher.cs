using Microsoft.Extensions.Logging;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;

namespace PropertyApi.Infrastructure.SocialDistribution.Publishing;

/// <summary>LinkedIn Company Page adapter. Placeholder — see <see cref="PlatformNotConfiguredPublisherBase"/>.</summary>
public sealed class LinkedInPublisher : PlatformNotConfiguredPublisherBase
{
    public override SocialPlatform Platform => SocialPlatform.LinkedIn;

    public LinkedInPublisher(ILogger<LinkedInPublisher> logger) : base(logger) { }

    public override SocialPublisherCapabilities GetCapabilities() => new(
        SupportsText: true,
        SupportsImages: true,
        SupportsVideo: true,
        SupportsStories: false,
        SupportsHashtags: true,
        SupportsScheduling: true,
        SupportsUpdate: true,
        SupportsDelete: true,
        MaxTextLength: 150,
        MaxImages: 9,
        RequiresImage: false);
}
