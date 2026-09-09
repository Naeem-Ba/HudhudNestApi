namespace PropertyApi.Domain.SocialDistribution.Policies;

/// <summary>
/// Exponential backoff with jitter for retryable publish failures (spec §12). Pure computation —
/// no I/O, no persistence — called by the command handler right before
/// <see cref="Entities.SocialPublication.MarkFailed"/>, which is what actually decides (based on
/// <see cref="Enums.SocialPublicationErrorCodeExtensions.IsRetryable"/> and MaxRetryCount)
/// whether this delay is even used.
///
/// Phase 6 spec §12: "لا تضع القيم داخل الكود بشكل غير قابل للتهيئة — استخدم Configuration".
/// <see cref="DefaultBaseDelay"/>/<see cref="DefaultMaxDelay"/>/<see cref="DefaultMaxJitterMilliseconds"/>
/// remain the zero-configuration fallback (and are exactly what every existing caller/test uses),
/// while <see cref="ComputeDelay(int,TimeSpan?,TimeSpan?,int?)"/>'s optional parameters let a
/// caller that reads <c>SocialDistributionRetryOptions</c> (Application/Infrastructure) override
/// them per environment — without turning this Domain policy into a stateful, DI-registered
/// service, which would break its "pure, no-database-needed" testability.
/// </summary>
public static class SocialPublicationRetryPolicy
{
    public static readonly TimeSpan DefaultBaseDelay = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromHours(6);
    public const int DefaultMaxJitterMilliseconds = 30_000;

    private const int MaxExponent = 10; // 2 min * 2^10 already exceeds MaxDelay, so this is just an overflow guard.

    /// <param name="retryCountBeforeThisFailure">SocialPublication.RetryCount at the moment of failure, i.e. how many retries have already happened.</param>
    /// <param name="baseDelay">Overrides <see cref="DefaultBaseDelay"/> when supplied — e.g. from configuration.</param>
    /// <param name="maxDelay">Overrides <see cref="DefaultMaxDelay"/> when supplied.</param>
    /// <param name="maxJitterMilliseconds">Overrides <see cref="DefaultMaxJitterMilliseconds"/> when supplied.</param>
    public static TimeSpan ComputeDelay(
        int retryCountBeforeThisFailure,
        TimeSpan? baseDelay = null,
        TimeSpan? maxDelay = null,
        int? maxJitterMilliseconds = null)
    {
        var effectiveBase = baseDelay ?? DefaultBaseDelay;
        var effectiveMax = maxDelay ?? DefaultMaxDelay;
        var effectiveJitter = Math.Max(maxJitterMilliseconds ?? DefaultMaxJitterMilliseconds, 0);

        var exponent = Math.Min(Math.Max(retryCountBeforeThisFailure, 0), MaxExponent);
        var raw = effectiveBase.TotalMilliseconds * Math.Pow(2, exponent);
        var capped = Math.Min(raw, effectiveMax.TotalMilliseconds);

        // Jitter avoids every failed publication in one batch retrying at the exact same instant
        // and hammering the platform (and this process's own worker) all at once again.
        var jitterMs = effectiveJitter == 0 ? 0 : Random.Shared.Next(0, effectiveJitter);

        return TimeSpan.FromMilliseconds(capped + jitterMs);
    }
}
