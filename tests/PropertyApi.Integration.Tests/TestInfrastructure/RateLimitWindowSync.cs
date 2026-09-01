namespace PropertyApi.Integration.Tests.TestInfrastructure;

/// <summary>
/// BUG-33: <c>RedisRateLimitingMiddleware</c> buckets requests into fixed windows aligned
/// to absolute UTC epoch time (<c>now.ToUnixTimeMilliseconds() / windowMs</c>), not to when
/// a given test's request loop happens to start. A test that fires a fixed number of
/// requests and asserts the last one trips the limit is only correct if the whole loop
/// stays inside a single window; if it straddles a window boundary the counter resets
/// mid-loop and the test flakes independently of any CI resource contention.
/// <see cref="WaitForSafeWindowStartAsync"/> uses the exact same bucket arithmetic as the
/// middleware to wait out any unsafe sliver of time near a boundary before a test starts,
/// removing that race by construction instead of hoping it doesn't happen.
/// </summary>
internal static class RateLimitWindowSync
{
    /// <summary>
    /// If fewer than <paramref name="minimumRemaining"/> (default 5s) remain until the
    /// current fixed window for <paramref name="window"/> rolls over, waits until just
    /// after the next window starts. Otherwise returns immediately. Bounds the worst-case
    /// wait to roughly <paramref name="minimumRemaining"/>, never to a full window.
    /// </summary>
    public static async Task WaitForSafeWindowStartAsync(
        TimeSpan window,
        TimeSpan? minimumRemaining = null)
    {
        var margin = minimumRemaining ?? TimeSpan.FromSeconds(5);
        var windowMs = checked((long)window.TotalMilliseconds);
        var marginMs = checked((long)margin.TotalMilliseconds);

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var msIntoWindow = nowMs % windowMs;
        var msRemaining = windowMs - msIntoWindow;

        if (msRemaining >= marginMs)
        {
            return;
        }

        // Small buffer past the boundary so a slow clock/scheduler tick can't put us
        // right back in the unsafe sliver we just waited out.
        await Task.Delay((int)(msRemaining + 250));
    }
}
