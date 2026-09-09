using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Application.SocialDistribution.Interfaces;

/// <summary>
/// Resolves the <see cref="ISocialPublisher"/> for a given platform — the ONE place that maps
/// "platform enum value" to "concrete adapter instance" (Phase 5 spec §6). Every caller
/// (<c>PublishSocialPublicationCommandHandler</c>, previews, capability lookups) depends on this
/// interface only, never on <c>IEnumerable&lt;ISocialPublisher&gt;</c> or an if/else over
/// <see cref="SocialPlatform"/> — adding a new platform is "implement <see cref="ISocialPublisher"/>
/// + register it", never a change to any consumer of this registry.
/// </summary>
public interface ISocialPublisherRegistry
{
    /// <summary>All platforms with a registered publisher (used by the /publishers listing endpoint).</summary>
    IReadOnlyCollection<SocialPlatform> SupportedPlatforms { get; }

    bool HasPublisher(SocialPlatform platform);

    /// <summary>Never null when <see cref="HasPublisher"/> is true for the same platform.</summary>
    ISocialPublisher? TryGetPublisher(SocialPlatform platform);
}
