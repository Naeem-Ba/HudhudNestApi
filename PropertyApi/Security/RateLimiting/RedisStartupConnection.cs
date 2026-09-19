namespace PropertyApi.Security.RateLimiting;

/// <summary>
/// Gives the Redis connection a bounded time to establish before the host starts listening.
/// With abortConnect=false <c>ConnectionMultiplexer.Connect</c> returns before the socket is up,
/// so without this the first rate-limited requests on a fresh instance hit the limiter's
/// intentional fail-closed 503 while readiness already answers 200.
/// </summary>
public static class RedisStartupConnection
{
    public const string TimeoutConfigurationKey = "Redis:StartupConnectTimeoutSeconds";
    public const int DefaultTimeoutSeconds = 15;
    public const int MaximumTimeoutSeconds = 60;

    public static int ResolveTimeoutSeconds(IConfiguration configuration) =>
        Math.Clamp(
            configuration.GetValue<int?>(TimeoutConfigurationKey) ?? DefaultTimeoutSeconds,
            0,
            MaximumTimeoutSeconds);

    /// <returns><c>true</c> when the connection was established within the budget.</returns>
    public static async Task<bool> WaitUntilConnectedAsync(
        Func<bool> isConnected,
        TimeSpan budget,
        TimeSpan pollInterval,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + budget;
        while (!isConnected())
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        return true;
    }
}
