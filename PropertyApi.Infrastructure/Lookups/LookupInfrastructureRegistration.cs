using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Valuation.Interfaces;

namespace PropertyApi.Infrastructure.Lookups;

internal static class LookupInfrastructureRegistration
{
    public static IServiceCollection AddLookupInfrastructure(
        this IServiceCollection services)
    {
        services.AddScoped<ICommonLookupService, CommonLookupService>();
        services.AddScoped<ILocationHierarchyChecker, LocationHierarchyChecker>();
        services.AddScoped<ILocationSuggestionService, LocationSuggestionService>();

        // Stage 4 (Valuation Office Matching) Level 4 fallback — see
        // IGovernorateNeighborProvider's doc comment for why this lives here.
        services.AddScoped<IGovernorateNeighborProvider, GovernorateNeighborProvider>();

        return services;
    }
}
