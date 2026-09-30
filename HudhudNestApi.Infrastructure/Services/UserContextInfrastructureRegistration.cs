using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Common.Interfaces;

namespace HudhudNestApi.Infrastructure.Services;

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
