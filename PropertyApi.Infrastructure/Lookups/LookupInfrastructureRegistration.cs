using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Application.Common.Interfaces;

namespace PropertyApi.Infrastructure.Lookups;

internal static class LookupInfrastructureRegistration
{
    public static IServiceCollection AddLookupInfrastructure(
        this IServiceCollection services)
    {
        services.AddScoped<ICommonLookupService, CommonLookupService>();

        return services;
    }
}
