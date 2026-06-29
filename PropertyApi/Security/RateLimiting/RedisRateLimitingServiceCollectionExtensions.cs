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
        var redisConnectionString =
            configuration.GetConnectionString("Redis")
            ?? configuration["Redis:ConnectionString"]
            ?? configuration["RedisRateLimiting:ConnectionString"]
            ?? configuration["REDIS_CONNECTION_STRING"]
            ?? configuration["REDIS_URL"];

        services.AddOptions<RedisRateLimitingOptions>()
            .Bind(configuration.GetSection(RedisRateLimitingOptions.SectionName))
            .PostConfigure(options =>
            {
                options.ConnectionString ??= redisConnectionString;

                if (string.IsNullOrWhiteSpace(options.InstanceName))
                    options.InstanceName = "RateLimit:";

                foreach (var policy in RedisRateLimitingDefaults.Policies)
                {
                    if (!options.Policies.ContainsKey(policy.Key))
                        options.Policies[policy.Key] = policy.Value;
                }

                // مهم جدًا:
                // في غير الإنتاج، لا تجعل غياب Redis يكسر التطبيق.
                if (!environment.IsProduction() && string.IsNullOrWhiteSpace(options.ConnectionString))
                {
                    options.Enabled = false;
                }
            })
            .Validate(options =>
                !options.Enabled || !string.IsNullOrWhiteSpace(options.ConnectionString),
                "Redis rate limiting is enabled, but no Redis connection string is configured.")
            .Validate(options =>
                options.Policies.Values.All(policy =>
                    policy.PermitLimit > 0 && policy.WindowSeconds > 0),
                "Redis rate limit policies must define PermitLimit > 0 and WindowSeconds > 0.")
            .ValidateOnStart();

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RedisRateLimitingOptions>>().Value;

            if (!options.Enabled)
            {
                throw new InvalidOperationException(
                    "Redis rate limiting is disabled. IConnectionMultiplexer should not be resolved.");
            }

            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException(
                    "Redis rate limiting is enabled, but Redis connection string is missing.");
            }

            var redisOptions = ConfigurationOptions.Parse(options.ConnectionString);

            redisOptions.AbortOnConnectFail = false;
            redisOptions.ConnectRetry = 3;
            redisOptions.ConnectTimeout = 5000;
            redisOptions.SyncTimeout = 5000;

            return ConnectionMultiplexer.Connect(redisOptions);
        });

        return services;
    }
}