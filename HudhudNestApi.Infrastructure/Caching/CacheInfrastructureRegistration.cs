using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HudhudNestApi.Infrastructure.Caching;

internal static class CacheInfrastructureRegistration
{
    public static IServiceCollection AddCacheInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? configuration["Redis:ConnectionString"];

        var isTestingOrCi =
            environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase) ||
            environment.EnvironmentName.Equals("CI", StringComparison.OrdinalIgnoreCase);

        var requiresRedis =
            configuration.GetValue<bool?>("Redis:Required")
            ?? (environment.IsProduction() ||
                environment.IsStaging());

        if (requiresRedis)
        {
            if (string.IsNullOrWhiteSpace(redisConnectionString))
            {
                throw new InvalidOperationException(
                    "Redis is required in Staging and Production. " +
                    "Configure ConnectionStrings:Redis or " +
                    "Redis:ConnectionString.");
            }

            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = "HudhudNestApi:";
            });
        }
        else if (!isTestingOrCi &&
                 !string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = "HudhudNestApi:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        return services;
    }
}
