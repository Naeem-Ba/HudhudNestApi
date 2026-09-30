using Microsoft.Extensions.Options;

namespace HudhudNestApi.Security.RateLimiting;

public static class RedisRateLimitingApplicationBuilderExtensions
{
    public static IApplicationBuilder UseRedisRateLimiting(this IApplicationBuilder app)
        => app.UseMiddleware<RedisRateLimitingMiddleware>();

    /// <summary>
    /// Whether the Redis-backed limiter gates requests, decided from the built host's final configuration:
    /// the same bound <see cref="RedisRateLimitingOptions"/> the middleware reads on every request.
    /// Program.cs used to decide from the configuration as it stood before the host was built. When a later
    /// source switched Redis off (a test host's in-memory settings under CI's RateLimiting__Redis__Enabled=true),
    /// the Redis middleware was mounted instead of the in-memory limiter and then let every request through
    /// because its options said disabled: no rate limiting at all. Deciding here means exactly one limiter runs.
    /// </summary>
    public static bool IsRedisRateLimitingActive(this IServiceProvider services)
        => services.GetService<RedisRateLimitingRegistration>() is not null &&
           services.GetRequiredService<IOptions<RedisRateLimitingOptions>>().Value.Enabled;
}

/// <summary>Marks that AddHudhudNestApiRedisRateLimiting ran; IOptions resolves (with defaults) even when it did not.</summary>
public sealed class RedisRateLimitingRegistration;
