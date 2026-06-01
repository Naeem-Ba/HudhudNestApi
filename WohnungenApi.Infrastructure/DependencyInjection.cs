using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WohnungenApi.Application.Common.Interfaces;
using WohnungenApi.Application.Listings.Interfaces;
using WohnungenApi.Application.Users.Interfaces;
using WohnungenApi.Application.Users.Messaging.Interfaces;
using WohnungenApi.Domain.Users.Entities;
using WohnungenApi.Infrastructure.Persistence;
using WohnungenApi.Infrastructure.Repositories;
using WohnungenApi.Infrastructure.Services;

namespace WohnungenApi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // ── Database ─────────────────────────────────────────────
        services.AddDbContext<AppDbContext>(options =>
        {
            var connectionString = ResolveConnectionString(configuration, environment);
            options.UseNpgsql(connectionString);

            if (environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        // ── ASP.NET Identity ─────────────────────────────────────
        services.AddIdentity<User, IdentityRole<Guid>>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = true;
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        // ── Repositories ─────────────────────────────────────────
        services.AddScoped<IPropertyRepository, PropertyRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();  // ← ADDED

        // ── Unit of Work ─────────────────────────────────────────
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // ── Current User Service ─────────────────────────────────
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        return services;
    }

    // ── Connection String Resolver ─────────────────────────────
    private static string ResolveConnectionString(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (environment.IsProduction())
        {
            var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL")
                ?? throw new InvalidOperationException(
                    "DATABASE_URL environment variable is required in Production.");

            if (databaseUrl.StartsWith("postgres://") ||
                databaseUrl.StartsWith("postgresql://"))
            {
                var uri = new Uri(databaseUrl);
                var db = uri.AbsolutePath.TrimStart('/');
                var user = uri.UserInfo.Split(':')[0];
                var pass = uri.UserInfo.Split(':')[1];
                var port = uri.Port > 0 ? uri.Port : 5432;

                return $"Host={uri.Host};Port={port};Database={db};" +
                       $"Username={user};Password={pass};" +
                       $"SSL Mode=Require;Trust Server Certificate=true;";
            }

            return databaseUrl;
        }

        return configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is missing in appsettings.json.");
    }
}