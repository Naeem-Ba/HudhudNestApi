namespace HudhudNestApi.Application.Auth.Interfaces;

/// <summary>
/// How many address-confirmation e-mails one account may be sent, and over how long.
/// </summary>
public static class EmailConfirmationSendPolicy
{
    public const int MaxSends = 3;

    public static readonly TimeSpan Window = TimeSpan.FromHours(24);
}

/// <summary>
/// Caps the confirmation e-mails one ACCOUNT can be sent, however they were asked for:
/// registration, "add / resend e-mail" from account settings, or the anonymous resend page.
///
/// The HTTP rate limiter cannot do this job. Its key is the caller's address (plus the user id in
/// Redis mode), its window is aligned to the clock rather than to the first request, it counts
/// requests that never send anything, and the only limit that existed for "add e-mail" was none at
/// all. This counts real sends, per account, over a rolling window.
/// </summary>
public interface IEmailConfirmationSendLimiter
{
    /// <summary>
    /// Counts one send. Returns null when it is allowed (and now recorded); otherwise how long until
    /// the oldest recorded send leaves the window and a new one is possible.
    /// </summary>
    Task<TimeSpan?> TryAcquireAsync(Guid identityId, CancellationToken ct = default);

    /// <summary>The same decision as <see cref="TryAcquireAsync"/>, without recording anything.</summary>
    Task<TimeSpan?> PeekAsync(Guid identityId, CancellationToken ct = default);
}

/// <summary>
/// Thrown by <see cref="IEmailVerificationService"/> instead of sending when the account has used up
/// <see cref="EmailConfirmationSendPolicy.MaxSends"/>. Callers that must not reveal anything
/// (registration, anonymous resend) already swallow every send failure; add-email turns it into 429.
/// </summary>
public sealed class EmailConfirmationLimitExceededException : Exception
{
    public EmailConfirmationLimitExceededException(TimeSpan retryAfter)
        : base("The confirmation e-mail limit for this account has been reached.")
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan RetryAfter { get; }
}
