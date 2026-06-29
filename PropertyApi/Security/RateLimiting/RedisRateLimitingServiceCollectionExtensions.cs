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

        //services.AddSingleton<IConnectionMultiplexer>(sp =>
        //{
        //    var options = sp.GetRequiredService<IOptions<RedisRateLimitingOptions>>().Value;
        //    return ConnectionMultiplexer.Connect(options.ConnectionString!);
        //});
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger("RedisRateLimiting");

            var options = sp.GetRequiredService<IOptions<RedisRateLimitingOptions>>().Value;

            if (string.IsNullOrWhiteSpace(options.ConnectionString))
                throw new InvalidOperationException("Redis connection string is missing.");

            var redisOptions = ConfigurationOptions.Parse(options.ConnectionString);

            redisOptions.AbortOnConnectFail = false;
            redisOptions.ConnectRetry = 3;
            redisOptions.ConnectTimeout = 5000;
            redisOptions.SyncTimeout = 5000;

            var multiplexer = ConnectionMultiplexer.Connect(redisOptions);

            logger.LogInformation(
                "Redis rate limiting initialized. IsConnected={IsConnected}, Endpoints={Endpoints}",
                multiplexer.IsConnected,
                string.Join(",", multiplexer.GetEndPoints().Select(e => e.ToString())));

            return multiplexer;
        });

        return services;
    }
}
