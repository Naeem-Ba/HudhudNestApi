using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Application.Common.Interfaces;

namespace PropertyApi.Infrastructure.Services;

internal static class UserContextInfrastructureRegistration
{
    public static IServiceCollection AddUserContextInfrastructure(
        this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        return services;
    }
}
