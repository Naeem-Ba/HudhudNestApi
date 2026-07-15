using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Infrastructure.Identity.Entities;
using InfrastructureApplicationRole = PropertyApi.Infrastructure.Identity.Entities.ApplicationRole;

namespace PropertyApi.Infrastructure.Persistence;

internal static class PersistenceInfrastructureRegistration
{
    public static IServiceCollection AddPersistenceInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            var connectionString =
                PostgresConnectionStringResolver.Resolve(
                    configuration,
                    environment);

            options.UseNpgsql(connectionString);

            if (environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        services.AddIdentity<ApplicationUser, InfrastructureApplicationRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = true;
            options.User.RequireUniqueEmail = false;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
