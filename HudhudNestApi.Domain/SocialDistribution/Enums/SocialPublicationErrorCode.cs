namespace HudhudNestApi.Domain.SocialDistribution.Enums;

/// <summary>
/// Classifies why a publish attempt failed. Set by an <see cref="Interfaces.ISocialPublisher"/>
/// (via <see cref="Models.SocialPublishResult"/>) on failure, never guessed by the entity itself.
/// See <see cref="SocialPublicationErrorCodeExtensions.IsRetryable"/> for which of these the
/// worker is allowed to automatically retry.
/// </summary>
public enum SocialPublicationErrorCode
{
    // ── Retryable: transient, may succeed on a later attempt ───────────────
    NetworkError = 1,
    Timeout = 2,
    RateLimited = 3,
    TemporaryUnavailable = 4,
    ServiceUnavailable = 5,

    // ── Non-retryable: will fail again unless something changes first ──────
    InvalidCredentials = 6,
    InvalidContent = 7,
    PermissionDenied = 8,
    AccountSuspended = 9,
    InvalidTargetUrl = 10,
    UnsupportedMedia = 11,
    PropertyNotPublic = 12,
    PlatformNotConfigured = 13,

    /// <summary>
    /// The worker process was interrupted (crash/restart) while a publish attempt was in flight —
    /// whether the platform actually received the post is unknown. Never auto-retried: retrying a
    /// call that may have already succeeded risks a real duplicate post, which this system must
    /// never do (spec: never fabricate success, and symmetrically, never risk a silent double
    /// post). Surfaced as a dead letter for a human to check the platform and resolve manually.
    /// </summary>
    AmbiguousOutcome = 14,
}

public static class SocialPublicationErrorCodeExtensions
{
    /// <summary>
    /// True for a transient error worth automatically retrying (with backoff), false for one
    /// that will keep failing until a human or a separate process fixes the underlying cause
    /// (bad credentials, suspended account, content rejected, property no longer public, ...).
    /// </summary>
    public static bool IsRetryable(this SocialPublicationErrorCode errorCode) => errorCode switch
    {
        SocialPublicationErrorCode.NetworkError => true,
        SocialPublicationErrorCode.Timeout => true,
        SocialPublicationErrorCode.RateLimited => true,
        SocialPublicationErrorCode.TemporaryUnavailable => true,
        SocialPublicationErrorCode.ServiceUnavailable => true,
        _ => false,
    };
}
