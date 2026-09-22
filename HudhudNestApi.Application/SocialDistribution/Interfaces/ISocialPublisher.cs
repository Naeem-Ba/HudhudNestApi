using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Models;

namespace HudhudNestApi.Application.SocialDistribution.Interfaces;

/// <summary>
/// The single seam between this bounded context and any real social platform (Phase 5 spec §3-§4:
/// Ports and Adapters). Domain never references this; Application depends only on this
/// abstraction (via <see cref="ISocialPublisherRegistry"/>, never a platform-specific type);
/// Infrastructure supplies one concrete implementation per platform (today:
/// <c>FacebookPublisher</c>/<c>InstagramPublisher</c>/<c>TelegramPublisher</c>/<c>TikTokPublisher</c>/
/// <c>YouTubePublisher</c>/<c>LinkedInPublisher</c> — every one of them still a safe stub, since no
/// official platform API integration exists yet; see their shared base class for what is real vs.
/// placeholder, and docs for how a real Facebook/Instagram/... publisher plugs in later).
/// </summary>
public interface ISocialPublisher
{
    SocialPlatform Platform { get; }

    /// <summary>
    /// What this platform actually supports — checked by the caller before a job is even created
    /// for a capability the platform lacks (spec §7).
    /// </summary>
    SocialPublisherCapabilities GetCapabilities();

    /// <summary>
    /// Local, no-network-call check that <paramref name="request"/> is something this platform
    /// could plausibly accept (length limits, required image, ...) — called BEFORE
    /// <see cref="PublishAsync"/> so an invalid request never burns a real API call or a retry
    /// attempt (spec §7: "لا ترسل الطلب الخارجي" when content is invalid). Pure/no I/O.
    /// </summary>
    SocialContentValidationResult ValidateContent(SocialPublishRequest request);

    /// <summary>
    /// Attempts one publish. MUST NOT throw for an ordinary platform-side failure (rate limit,
    /// bad credentials, rejected content, ...) — those come back as a
    /// <see cref="SocialPublishResult.Failure"/> so the caller's state machine can classify and
    /// (maybe) retry them. Only an unexpected/programming error should propagate as an exception.
    /// </summary>
    Task<SocialPublishResult> PublishAsync(SocialPublishRequest request, CancellationToken ct = default);

    /// <summary>
    /// Rewrites an already-published post's text/image (Phase 11 spec §7) — the caller MUST check
    /// <see cref="SocialPublisherCapabilities.SupportsUpdate"/> first; calling this on a platform
    /// that doesn't support it is a caller bug, not a normal failure path, though a safe
    /// implementation must still return a classified failure rather than throw.
    /// </summary>
    Task<SocialPublishResult> UpdateAsync(SocialPublishRequest request, string externalPostId, CancellationToken ct = default);

    /// <summary>Adds a comment beneath an already-published post (Phase 11 spec §7) — same capability-gating contract as <see cref="UpdateAsync"/>.</summary>
    Task<SocialPublishResult> CommentAsync(string externalPostId, string commentBody, CancellationToken ct = default);

    /// <summary>Removes an already-published post (Phase 11 spec §7) — same capability-gating contract as <see cref="UpdateAsync"/>, gated by <see cref="SocialPublisherCapabilities.SupportsDelete"/>.</summary>
    Task<SocialPublishResult> DeleteAsync(string externalPostId, CancellationToken ct = default);
}
