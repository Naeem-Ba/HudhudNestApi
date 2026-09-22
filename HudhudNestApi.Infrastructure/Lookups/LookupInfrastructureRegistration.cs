using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Valuation.Interfaces;

namespace HudhudNestApi.Infrastructure.Lookups;

internal static class LookupInfrastructureRegistration
{
    public static IServiceCollection AddLookupInfrastructure(
        this IServiceCollection services)
    {
        services.AddScoped<ICommonLookupService, CommonLookupService>();
        services.AddScoped<ILocationSuggestionService, LocationSuggestionService>();

        // Stage 4 (Valuation Office Matching) Level 4 fallback — see
        // IGovernorateNeighborProvider's doc comment for why this lives here.
        services.AddScoped<IGovernorateNeighborProvider, GovernorateNeighborProvider>();

        return services;
    }
}
