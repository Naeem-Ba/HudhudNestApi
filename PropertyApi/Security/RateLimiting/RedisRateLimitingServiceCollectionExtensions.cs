using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace PropertyApi.Security.RateLimiting;

public static class RedisRateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddPropertyApiRedisRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? configuration["Redis:ConnectionString"];

        services.AddOptions<RedisRateLimitingOptions>()
            .Bind(configuration.GetSection(RedisRateLimitingOptions.SectionName))
            .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.ConnectionString),
                "Redis rate limiting is enabled, but no Redis connection string is configured.")
            .Validate(options => options.Policies.Values.All(policy => policy.PermitLimit > 0 && policy.WindowSeconds > 0),
                "Redis rate limit policies must define PermitLimit > 0 and WindowSeconds > 0.")
            .ValidateOnStart();

        services.PostConfigure<RedisRateLimitingOptions>(options =>
        {
            options.ConnectionString ??= redisConnectionString;

            if (string.IsNullOrWhiteSpace(options.InstanceName))
                options.InstanceName = "RateLimit:";

            foreach (var policy in RedisRateLimitingDefaults.Policies)
            {
                if (!options.Policies.ContainsKey(policy.Key))
                    options.Policies[policy.Key] = policy.Value;
            }
        });

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RedisRateLimitingOptions>>().Value;
            return ConnectionMultiplexer.Connect(options.ConnectionString!);
        });

        return services;
    }
}
