using Microsoft.Extensions.DependencyInjection;

namespace HudhudNestApi.Infrastructure.Health;

internal static class HealthInfrastructureRegistration
{
    public static IServiceCollection AddOperationalHealthChecks(
        this IServiceCollection services)
    {
        services.AddScoped<PostGisHealthCheck>();
        services.AddHostedService<ProductionStartupValidator>();

        services.AddHealthChecks()
            .AddCheck<PostGisHealthCheck>(
                "postgresql-postgis",
                tags: ["ready", "db"])
            .AddCheck<DistributedCacheHealthCheck>(
                "redis",
                tags: ["ready", "cache"]);

        return services;
    }
}
