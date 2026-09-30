using StackExchange.Redis;

namespace HudhudNestApi.Security.RateLimiting;

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

    /// <summary>
    /// Waits (bounded) for <paramref name="multiplexer"/> to connect and logs the outcome.
    /// Never throws for an unreachable Redis: the instance still starts and the limiter keeps
    /// failing closed until the connection comes up.
    /// </summary>
    public static async Task<bool> EnsureConnectedAsync(
        IConnectionMultiplexer multiplexer,
        IConfiguration configuration,
        ILogger logger,
        TimeSpan? pollInterval = null)
    {
        var budgetSeconds = ResolveTimeoutSeconds(configuration);
        var connected = await WaitUntilConnectedAsync(
            () => multiplexer.IsConnected,
            TimeSpan.FromSeconds(budgetSeconds),
            pollInterval ?? TimeSpan.FromMilliseconds(100));

        if (connected)
        {
            logger.LogInformation("Redis connected before accepting traffic.");
        }
        else
        {
            logger.LogWarning(
                "Redis was not connected after {Seconds}s; starting anyway. Rate-limited endpoints will answer 503 until it connects.",
                budgetSeconds);
        }

        return connected;
    }

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
