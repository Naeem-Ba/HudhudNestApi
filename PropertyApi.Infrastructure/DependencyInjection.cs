using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Contact.Interfaces;
using PropertyApi.Application.Favorites.Interfaces;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Application.Users.Messaging.Interfaces;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Notifications.Interfaces;
using PropertyApi.Application.Bookings.Interfaces;
using PropertyApi.Application.Reviews.Interfaces;
using PropertyApi.Infrastructure.Bookings;
using PropertyApi.Infrastructure.Reviews;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Infrastructure.Repositories;
using PropertyApi.Infrastructure.Services;
using PropertyApi.Infrastructure.Media;
using PropertyApi.Infrastructure.Identity.Services;
using PropertyApi.Infrastructure.Auth.Services;
using PropertyApi.Infrastructure.Email;
using PropertyApi.Infrastructure.Auth.Repositories;
using PropertyApi.Infrastructure.Notifications;
using PropertyApi.Infrastructure.Lookups;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Infrastructure.Admin;
using IdentityEmailSender = Microsoft.AspNetCore.Identity.UI.Services.IEmailSender;
using Microsoft.Extensions.Options;
using PropertyApi.Infrastructure.Health;


using PropertyApi.Application.Analytics.Interfaces;
using PropertyApi.Infrastructure.Analytics;
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
        services.AddScoped<IIdentityUserService, IdentityUserService>();
        services.AddScoped<IIdentityRoleService, IdentityRoleService>();

        services.AddScoped<IUserSecurityStampReader, IdentitySecurityStampReader>();
        services.AddScoped<IUserSecurityStampValidator, CachedSecurityStampValidator>();

        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPasswordResetUrlBuilder, PasswordResetUrlBuilder>();
        services.AddScoped<IJwtTokenSettings, JwtTokenSettings>();
        services.AddScoped<IAdminUserQueryRepository, AdminUserQueryRepository>();
        services.AddScoped<IAdminIdentityService, AdminIdentityService>();
        services.AddScoped<IPropertyGeoSearchRepository, PropertyGeoSearchRepository>();
        services.AddScoped<IAnalyticsReadRepository, AnalyticsReadRepository>();

        services.AddScoped<IVisitRepository, VisitRepository>();
        services.AddScoped<IPropertyReviewRepository, PropertyReviewRepository>();

        services.AddHostedService<OtpCleanupHostedService>();

        // â”€â”€ Email ---------------------------------------------------
        services.Configure<EmailOptions>(
            configuration.GetSection(EmailOptions.SectionName));

        if (environment.IsProduction())
{
    services.AddScoped<SmtpEmailSender>();

    services.AddScoped<IdentityEmailSender>(
        sp => sp.GetRequiredService<SmtpEmailSender>());

    services.AddScoped<IApplicationEmailSender>(
        sp => sp.GetRequiredService<SmtpEmailSender>());
}
else
{
    services.AddScoped<ConsoleEmailSender>();

    services.AddScoped<IdentityEmailSender>(
        sp => sp.GetRequiredService<ConsoleEmailSender>());

    services.AddScoped<IApplicationEmailSender>(
        sp => sp.GetRequiredService<ConsoleEmailSender>());
}

        // â”€â”€ OTP / SMS Auth Services â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        services.AddOptions<SmsProviderOptions>()
            .Bind(configuration.GetSection(SmsProviderOptions.SectionName));

        if (environment.IsProduction())
        {
            services.AddOptions<SmsProviderOptions>()
                .Bind(configuration.GetSection(SmsProviderOptions.SectionName))
                .Validate(options => !string.IsNullOrWhiteSpace(options.Provider), "SmsProvider:Provider is required in Production.")
                .Validate(options => !string.IsNullOrWhiteSpace(options.ApiUrl), "SmsProvider:ApiUrl is required in Production.")
                .Validate(options => Uri.TryCreate(options.ApiUrl, UriKind.Absolute, out var uri) &&
                                     (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp),
                    "SmsProvider:ApiUrl must be a valid absolute HTTP/HTTPS URL in Production.")
                .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "SmsProvider:ApiKey is required in Production.")
                .Validate(options => !string.IsNullOrWhiteSpace(options.FromNumber), "SmsProvider:FromNumber is required in Production.")
                .ValidateOnStart();
        }

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
        
        // â”€â”€ Database â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€ ASP.NET Identity â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // â”€â”€ Repositories â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        services.AddScoped<IPropertyRepository, PropertyRepository>();
        services.AddScoped<IPropertyImageRepository, PropertyImageRepository>();
        services.AddScoped<IFavoriteRepository, FavoriteRepository>();
        services.AddScoped<IContactMessageRepository, ContactMessageRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();  // â† ADDED

        // â”€â”€ Unit of Work â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // â”€â”€ Current User Service â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // â”€â”€ Cloudinary Media Service â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        services.Configure<CloudinaryOptions>(
            configuration.GetSection(CloudinaryOptions.SectionName));


        services.AddHttpClient<CloudinaryMediaStorageService>();
        services.AddScoped<IMediaStorageService>(sp =>
            sp.GetRequiredService<CloudinaryMediaStorageService>());

        var redisConnectionString = configuration.GetConnectionString("Redis")
    ?? configuration["Redis:ConnectionString"];

var isTestingOrCi =
    environment.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase) ||
    environment.EnvironmentName.Equals("CI", StringComparison.OrdinalIgnoreCase);

if (environment.IsProduction())
{
    if (string.IsNullOrWhiteSpace(redisConnectionString))
    {
        throw new InvalidOperationException(
            "Redis is required in Production for distributed security stamp caching. Configure ConnectionStrings:Redis or Redis:ConnectionString.");
    }

    services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "PropertyApi:";
    });
}
else if (!isTestingOrCi && !string.IsNullOrWhiteSpace(redisConnectionString))
{
    services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "PropertyApi:";
    });
}
else
{
    services.AddDistributedMemoryCache();
}
        services.AddScoped<ICommonLookupService, CommonLookupService>();

        services.AddScoped<PostGisHealthCheck>();
        services.AddHostedService<ProductionStartupValidator>();
        services.AddHealthChecks()
            .AddCheck<PostGisHealthCheck>("postgis");

        return services;
    }

    // â”€â”€ Connection String Resolver â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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
                       $"SSL Mode=Require;";
            }

            if (databaseUrl.Contains("Trust Server Certificate=true", StringComparison.OrdinalIgnoreCase) ||
                databaseUrl.Contains("TrustServerCertificate=true", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Production database connection must not contain Trust Server Certificate=true.");
            }

            if (!databaseUrl.Contains("SSL Mode=Require", StringComparison.OrdinalIgnoreCase) &&
                !databaseUrl.Contains("SSL Mode=VerifyFull", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Production database connection must explicitly use SSL Mode=Require or SSL Mode=VerifyFull.");
            }

            return databaseUrl;
        }

        return configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is missing in appsettings.json.");
    }
}



