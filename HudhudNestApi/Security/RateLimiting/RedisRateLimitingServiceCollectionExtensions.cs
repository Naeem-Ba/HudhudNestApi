using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace HudhudNestApi.Security.RateLimiting;

public static class RedisRateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddHudhudNestApiRedisRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var redisConnectionString = ResolveRedisConnectionString(configuration);

        services.AddOptions<RedisRateLimitingOptions>()
            .Bind(configuration.GetSection(RedisRateLimitingOptions.SectionName))
            .PostConfigure(options =>
            {
                // مهم جدًا:
                // لا تستخدم ??= هنا، لأن appsettings قد يحتوي localhost:6379.
                // إذا وُجد Redis من ENV/Render يجب أن يغلب دائمًا.
                if (!string.IsNullOrWhiteSpace(redisConnectionString))
                {
                    options.ConnectionString = redisConnectionString;
                }

                if (string.IsNullOrWhiteSpace(options.InstanceName))
                {
                    options.InstanceName = "RateLimit:";
                }

                foreach (var policy in RedisRateLimitingDefaults.Policies)
                {
                    if (!options.Policies.ContainsKey(policy.Key))
                    {
                        options.Policies[policy.Key] = policy.Value;
                    }
                }
            })
            .Validate(
                options => !options.Enabled || !string.IsNullOrWhiteSpace(options.ConnectionString),
                "Redis rate limiting is enabled, but no Redis connection string is configured.")
            .Validate(
                options => !options.Enabled ||
                           !environment.IsProduction() ||
                           !IsLocalhostRedis(options.ConnectionString),
                "Redis rate limiting is enabled in Production, but Redis points to localhost.")
            .Validate(
                options => options.Policies.Values.All(policy =>
                    policy.PermitLimit > 0 && policy.WindowSeconds > 0),
                "Redis rate limit policies must define PermitLimit > 0 and WindowSeconds > 0.")
            .ValidateOnStart();

        // TryAddSingleton, not AddSingleton: Program.cs already registers a general-purpose
        // IConnectionMultiplexer whenever a Redis connection string exists (used by tracing
        // instrumentation and the observability synthetic check, among others), independent
        // of whether Redis rate limiting itself is enabled. Reuse that one connection instead
        // of opening a second one when both happen to be active (Production).
        services.TryAddSingleton<IConnectionMultiplexer>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>()
                .CreateLogger("RedisRateLimiting");

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

            var redisOptions = RedisConfigurationOptionsFactory.Build(options.ConnectionString);

            var multiplexer = ConnectionMultiplexer.Connect(redisOptions);

            var ping = multiplexer.GetDatabase().Ping();

            logger.LogInformation(
                "Redis rate limiting initialized. IsConnected={IsConnected}, PingMs={PingMs}, Endpoints={Endpoints}",
                multiplexer.IsConnected,
                ping.TotalMilliseconds,
                string.Join(",", multiplexer.GetEndPoints().Select(e => e.ToString())));

            if (!multiplexer.IsConnected)
            {
                throw new InvalidOperationException(
                    "Redis rate limiting is enabled, but Redis is not connected.");
            }

            return multiplexer;
        });

        return services;
    }

    private static string? ResolveRedisConnectionString(IConfiguration configuration)
    {
        return configuration["RedisRateLimiting:ConnectionString"]
            ?? configuration.GetConnectionString("Redis")
            ?? configuration["Redis:ConnectionString"]
            ?? configuration["REDIS_CONNECTION_STRING"]
            ?? configuration["REDIS_URL"];
    }

    private static bool IsLocalhostRedis(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        return connectionString.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
               connectionString.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               connectionString.Contains("[::1]", StringComparison.OrdinalIgnoreCase);
    }
}