using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Infrastructure.SocialDistribution.Publishing;

/// <summary>
/// DI-backed <see cref="ISocialPublisherRegistry"/> — builds a Platform→Publisher lookup once
/// from whatever <see cref="ISocialPublisher"/> implementations are registered (Phase 5 spec §6).
/// Adding TikTok's real integration later is exactly: implement <see cref="ISocialPublisher"/>,
/// register it in <see cref="SocialDistributionInfrastructureRegistration"/> — this class needs
/// no change at all, and neither does anything that depends on it.
/// </summary>
public sealed class SocialPublisherRegistry : ISocialPublisherRegistry
{
    private readonly IReadOnlyDictionary<SocialPlatform, ISocialPublisher> _publishersByPlatform;

    public SocialPublisherRegistry(IEnumerable<ISocialPublisher> publishers)
    {
        ArgumentNullException.ThrowIfNull(publishers);

        // Last registration for a given platform wins — this is how a real
        // FacebookGraphApiSocialPublisher would be swapped in ahead of the stub for just that one
        // platform without removing the stub registration line for every other platform.
        _publishersByPlatform = publishers
            .GroupBy(p => p.Platform)
            .ToDictionary(g => g.Key, g => g.Last());
    }

    public IReadOnlyCollection<SocialPlatform> SupportedPlatforms => _publishersByPlatform.Keys.ToArray();

    public bool HasPublisher(SocialPlatform platform) => _publishersByPlatform.ContainsKey(platform);

    public ISocialPublisher? TryGetPublisher(SocialPlatform platform) =>
        _publishersByPlatform.TryGetValue(platform, out var publisher) ? publisher : null;
}
