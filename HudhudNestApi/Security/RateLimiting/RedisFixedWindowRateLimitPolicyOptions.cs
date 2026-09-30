namespace HudhudNestApi.Security.RateLimiting;

public sealed class RedisFixedWindowRateLimitPolicyOptions
{
    public int PermitLimit { get; set; }

    public int WindowSeconds { get; set; }

    /// <summary>
    /// Kept for configuration compatibility with ASP.NET Core fixed-window options.
    /// The Redis implementation intentionally rejects excess API requests instead
    /// of maintaining a distributed waiting queue.
    /// </summary>
    public int QueueLimit { get; set; }

    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);
}
