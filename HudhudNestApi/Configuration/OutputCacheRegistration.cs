using Microsoft.AspNetCore.OutputCaching;
using HudhudNestApi.Application.Common.Caching;

namespace HudhudNestApi.Configuration;

public static class OutputCacheRegistration
{
    public const string MarketInsightsPolicy = "market-insights";
    public const string PublicPropertyListPolicy = "public-property-list";
    public const string PublicPropertyDetailsPolicy = "public-property-details";

    public const string AnalyticsTag = "analytics";

    /// <summary>
    /// How long a fresh response may be served again before the server recomputes it. Shared
    /// with PropertiesController, which sets a browser/CDN Cache-Control: max-age of the same
    /// duration (OutputCache itself never sends that header -- see AnalyticsController's doc
    /// comment on why [OutputCache] replaced [ResponseCache]). Reusing the exact same duration
    /// for the browser-facing header is deliberate, not arbitrary: this server-side TTL is
    /// already the staleness window the system accepts for every visitor regardless of a
    /// single browser's own cache (two different users within the same window already see the
    /// same cached snapshot), so a browser honoring the identical window adds no staleness
    /// exposure beyond what's already designed in -- it only skips a round trip for requests
    /// that would have returned the same cached answer anyway.
    /// </summary>
    public static readonly TimeSpan PublicPropertyListMaxAge = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan PublicPropertyDetailsMaxAge = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Every camelCase query-string name PropertyFilterDto binds from GetAll's [FromQuery]
    /// parameter, used both to configure PublicPropertyListPolicy's SetVaryByQuery below and by
    /// OutputCacheVaryByQueryTests (reflects over PropertyFilterDto's public settable properties)
    /// to keep the two in sync. Do not edit SetVaryByQuery directly below -- add the property to
    /// PropertyFilterDto, add its camelCase name here, and the test enforces nothing is missed
    /// again the way it was before (session audit, 2026-10: searchTerm/governorateId/districtId/
    /// neighborhoodId/propertyTypeId/currencyCode/agencyId were silently left out, so two
    /// requests differing only in one of those params could collide on the same cache entry).
    /// </summary>
    public static readonly string[] PublicPropertyListVaryByQueryParams =
    [
        "page",
        "pageSize",
        "searchTerm",
        "countryCode",
        "city",
        "region",
        "governorateId",
        "districtId",
        "neighborhoodId",
        "propertyTypeId",
        "listingType",
        "status",
        "condition",
        "minPrice",
        "maxPrice",
        "currencyCode",
        "minRooms",
        "maxRooms",
        "minArea",
        "maxArea",
        "hasBalcony",
        "hasElevator",
        "hasParkingSpace",
        "amenityIds",
        "ownerId",
        "agencyId",
        "sortBy",
        "sortDescending"
    ];

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
                .Expire(PublicPropertyListMaxAge)
                .SetVaryByQuery(PublicPropertyListVaryByQueryParams)
                .Tag(OutputCacheTags.Properties));

        options.AddPolicy(
            PublicPropertyDetailsPolicy,
            policy => policy
                .Expire(PublicPropertyDetailsMaxAge)
                .Tag(OutputCacheTags.Properties));
    }
}