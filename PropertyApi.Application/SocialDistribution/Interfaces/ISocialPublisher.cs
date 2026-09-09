using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;

namespace PropertyApi.Application.SocialDistribution.Interfaces;

/// <summary>
/// The single seam between this bounded context and any real social platform (spec §11). Domain
/// never references this; Application depends only on this abstraction; Infrastructure supplies
/// the implementations (today: <c>StubSocialPublisher</c> only — see docs for how a real
/// Facebook/Instagram/... publisher plugs in later without touching Domain or Application).
/// </summary>
public interface ISocialPublisher
{
    SocialPlatform Platform { get; }

    /// <summary>
    /// Attempts one publish. MUST NOT throw for an ordinary platform-side failure (rate limit,
    /// bad credentials, rejected content, ...) — those come back as a
    /// <see cref="SocialPublishResult.Failure"/> so the caller's state machine can classify and
    /// (maybe) retry them. Only an unexpected/programming error should propagate as an exception.
    /// </summary>
    Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken ct = default);
}
