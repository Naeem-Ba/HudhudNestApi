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

    /// <summary>
    /// Counts this policy in the API process's memory instead of on Redis (launch audit A6-002).
    /// Used for the anonymous public read policies, so a public GET costs no Redis command.
    /// Exact with one API instance; with several, each counts on its own. Left unset in
    /// configuration, a policy keeps the default from RedisRateLimitingDefaults, so overriding
    /// only PermitLimit/WindowSeconds does not move a public read policy back to Redis.
    /// </summary>
    public bool? InProcess { get; set; }

    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);
}
