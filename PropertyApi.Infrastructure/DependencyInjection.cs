using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Application.Users.Messaging.Interfaces;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Infrastructure.Repositories;
using PropertyApi.Infrastructure.Services;
using PropertyApi.Infrastructure.Media;
using PropertyApi.Infrastructure.Identity.Services;
using Microsoft.AspNetCore.Identity.UI.Services;
using PropertyApi.Infrastructure.Auth.Services;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Infrastructure.Email;
using PropertyApi.Infrastructure.Auth.Repositories;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Infrastructure.Notifications;

namespace PropertyApi.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {

        services.AddOptions<JwtOptions>()
     .Bind(configuration.GetSection(JwtOptions.SectionName))
     .Validate(
         options =>
             !string.IsNullOrWhiteSpace(options.Key) &&
             options.Key.Length >= 32,
         "Jwt:Key must contain at least 32 characters.")
     .Validate(
         options => !string.IsNullOrWhiteSpace(options.Issuer),
         "Jwt:Issuer is required.")
     .Validate(
         options => !string.IsNullOrWhiteSpace(options.Audience),
         "Jwt:Audience is required.")
     .Validate(
         options => options.AccessTokenMinutes > 0,
         "Jwt:AccessTokenMinutes must be greater than zero.")
     .Validate(
         options => options.RefreshTokenDays > 0,
         "Jwt:RefreshTokenDays must be greater than zero.")
     .ValidateOnStart();

        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddScoped<INotificationService, NotificationService>();

        // ── Email ---------------------------------------------------
        services.Configure<EmailOptions>(
            configuration.GetSection(EmailOptions.SectionName));

        if (environment.IsProduction())
        {
            services.AddScoped<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddScoped<IEmailSender, ConsoleEmailSender>();
        }

        // ── OTP / SMS Auth Services ─────────────────────────────────
        services.AddScoped<IOtpCodeRepository, OtpCodeRepository>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<IEmailVerificationService, EmailVerificationService>();

        if (environment.IsDevelopment() ||
            environment.EnvironmentName == "Testing" ||
            environment.EnvironmentName == "CI")
        {
            services.AddScoped<ISmsService, ConsoleSmsService>();
        }
        else
        {
            services.AddHttpClient<ISmsService, HttpSmsService>();
        }
        
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
            options.User.RequireUniqueEmail = false;
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

        // ── Cloudinary Media Service ──────────────────────────────
        services.Configure<CloudinaryOptions>(
            configuration.GetSection(CloudinaryOptions.SectionName));

        // ملاحظة: هنا يُسجَّل كـ Scoped لأنه يُقرأ من IOptions (singleton)
        // CloudinaryMediaStorageService نفسه stateless تقريباً
        services.AddScoped<CloudinaryMediaStorageService>();


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
