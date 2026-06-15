namespace PropertyApi.Security.RateLimiting;

public sealed class RedisRateLimitingOptions
{
    public const string SectionName = "RateLimiting:Redis";

    public bool Enabled { get; set; } = true;

    public string? ConnectionString { get; set; }

    public string InstanceName { get; set; } = "RateLimit:";

    public Dictionary<string, RedisFixedWindowRateLimitPolicyOptions> Policies { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}
