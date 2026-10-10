using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Infrastructure.Identity.Entities;
using HudhudNestApi.Infrastructure.Performance;
using InfrastructureApplicationRole = HudhudNestApi.Infrastructure.Identity.Entities.ApplicationRole;

namespace HudhudNestApi.Infrastructure.Persistence;

internal static class PersistenceInfrastructureRegistration
{
    public static IServiceCollection AddPersistenceInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // The migrations and the model snapshot were generated with Npgsql's legacy timestamp
        // behaviour, and Program.cs sets the same switch for the API. A host that only calls
        // AddInfrastructure -- the Migrator tool -- never runs Program.cs, and EF Core 9+ refuses to
        // migrate when the model it builds differs from the snapshot (PendingModelChangesWarning).
        // Idempotent, and it has to happen before the first DbContext is created.
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var exposeDatabaseDiagnostics =
            PerformanceDatabaseDiagnosticsPolicy.IsEnabled(environment, configuration);
        if (exposeDatabaseDiagnostics)
            services.AddSingleton<PerformanceDatabaseCommandInterceptor>();

        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var connectionString =
                PostgresConnectionStringResolver.Resolve(
                    configuration,
                    environment);

            options.UseNpgsql(connectionString);

            if (exposeDatabaseDiagnostics)
                options.AddInterceptors(
                    serviceProvider.GetRequiredService<PerformanceDatabaseCommandInterceptor>());

            if (environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        services.AddIdentity<ApplicationUser, InfrastructureApplicationRole>(options =>
        {
            // Password Policy
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = true;
            options.User.RequireUniqueEmail = false;

            // Account Lockout Policy - متوازي مع تحديد المعدل القائم على IP
            // يحمي الحساب نفسه من محاولات التخمين الموزعة عبر عناوين IP متعددة
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
