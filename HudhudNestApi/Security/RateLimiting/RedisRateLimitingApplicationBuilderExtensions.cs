namespace HudhudNestApi.Security.RateLimiting;

public static class RedisRateLimitingApplicationBuilderExtensions
{
    public static IApplicationBuilder UseRedisRateLimiting(this IApplicationBuilder app)
        => app.UseMiddleware<RedisRateLimitingMiddleware>();
}
