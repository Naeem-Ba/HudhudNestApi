using Microsoft.AspNetCore.OutputCaching;

namespace HudhudNestApi.Configuration;

public static class OutputCacheRegistration
{
    public const string MarketInsightsPolicy = "market-insights";
    public const string PublicPropertyListPolicy = "public-property-list";
    public const string PublicPropertyDetailsPolicy = "public-property-details";

    public const string AnalyticsTag = "analytics";
    public const string PropertiesTag = "properties";

    public static IServiceCollection AddScaleOutOutputCaching(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var redisConnection =
            configuration.GetConnectionString("Redis") ??
            configuration["Redis:ConnectionString"];

        var isTestingOrCi =
            environment.EnvironmentName.Equals(
                "Testing",
                StringComparison.OrdinalIgnoreCase) ||
            environment.EnvironmentName.Equals(
                "CI",
                StringComparison.OrdinalIgnoreCase);

        var requiresRedis =
            environment.IsProduction() ||
            environment.IsStaging();

        if (string.IsNullOrWhiteSpace(redisConnection))
        {
            if (requiresRedis)
            {
                throw new InvalidOperationException(
                    "Redis is required for Output Cache in " +
                    "Staging and Production.");
            }

            services.AddOutputCache(ConfigurePolicies);
            return services;
        }

        services.AddStackExchangeRedisOutputCache(options =>
        {
            options.Configuration = redisConnection;
            options.InstanceName =
                $"HudhudNestApi:{environment.EnvironmentName}:v1:OutputCache:";
        });

        services.AddOutputCache(ConfigurePolicies);

        return services;
    }

    private static void ConfigurePolicies(
        OutputCacheOptions options)
    {
        options.DefaultExpirationTimeSpan =
            TimeSpan.FromSeconds(30);

        options.MaximumBodySize =
            2 * 1024 * 1024;

        options.SizeLimit =
            128 * 1024 * 1024;

        options.AddPolicy(
            MarketInsightsPolicy,
            policy => policy
                .Expire(TimeSpan.FromMinutes(5))
                .SetVaryByQuery("countryCode")
                .Tag(AnalyticsTag));

        options.AddPolicy(
            PublicPropertyListPolicy,
            policy => policy
                .Expire(TimeSpan.FromSeconds(30))
                .SetVaryByQuery(
                    "page",
                    "pageSize",
                    "countryCode",
                    "city",
                    "region",
                    "listingType",
                    "status",
                    "condition",
                    "minPrice",
                    "maxPrice",
                    "minRooms",
                    "maxRooms",
                    "minArea",
                    "maxArea",
                    "hasBalcony",
                    "hasElevator",
                    "hasParkingSpace",
                    "ownerId",
                    "amenityIds",
                    "sortBy",
                    "sortDescending")
                .Tag(PropertiesTag));

        options.AddPolicy(
            PublicPropertyDetailsPolicy,
            policy => policy
                .Expire(TimeSpan.FromSeconds(60))
                .Tag(PropertiesTag));
    }
}