namespace PropertyApi.Domain.SocialDistribution.Policies;

/// <summary>
/// Exponential backoff with jitter for retryable publish failures (spec §12). Pure computation —
/// no I/O, no persistence — called by the command handler right before
/// <see cref="Entities.SocialPublication.MarkFailed"/>, which is what actually decides (based on
/// <see cref="Enums.SocialPublicationErrorCodeExtensions.IsRetryable"/> and MaxRetryCount)
/// whether this delay is even used.
/// </summary>
public static class SocialPublicationRetryPolicy
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromHours(6);
    private const int MaxExponent = 10; // 2 min * 2^10 already exceeds MaxDelay, so this is just an overflow guard.

    /// <param name="retryCountBeforeThisFailure">SocialPublication.RetryCount at the moment of failure, i.e. how many retries have already happened.</param>
    public static TimeSpan ComputeDelay(int retryCountBeforeThisFailure)
    {
        var exponent = Math.Min(Math.Max(retryCountBeforeThisFailure, 0), MaxExponent);
        var raw = BaseDelay.TotalMilliseconds * Math.Pow(2, exponent);
        var capped = Math.Min(raw, MaxDelay.TotalMilliseconds);

        // Jitter avoids every failed publication in one batch retrying at the exact same instant
        // and hammering the platform (and this process's own worker) all at once again.
        var jitterMs = Random.Shared.Next(0, 30_000);

        return TimeSpan.FromMilliseconds(capped + jitterMs);
    }
}
