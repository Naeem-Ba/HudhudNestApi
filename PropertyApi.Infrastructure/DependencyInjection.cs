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
using Npgsql;


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
        services.AddScoped<IUserSecurityStampCacheInvalidator, DistributedSecurityStampCacheInvalidator>();

        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IPasswordResetUrlBuilder, PasswordResetUrlBuilder>();
        services.AddScoped<IJwtTokenSettings, JwtTokenSettings>();
        services.AddScoped<IAdminUserQueryRepository, AdminUserQueryRepository>();
        services.AddScoped<IAdminIdentityService, AdminIdentityService>();
        services.AddScoped<IPropertyGeoSearchRepository, PropertyGeoSearchRepository>();
        services.AddScoped<IAnalyticsReadRepository, AnalyticsReadRepository>();
        services.AddScoped<IPropertyReadRepository, PropertyReadRepository>();

        services.AddScoped<IVisitRepository, VisitRepository>();
        services.AddScoped<IPropertyReviewRepository, PropertyReviewRepository>();

        services.AddHostedService<OtpCleanupHostedService>();

        // Ã¢â€â‚¬Ã¢â€â‚¬ Email ---------------------------------------------------
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

        // Ã¢â€â‚¬Ã¢â€â‚¬ OTP / SMS Auth Services Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
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

        services.AddDbContext<AppDbContext>(options =>
        {
            var connectionString = ResolveConnectionString(configuration, environment);

            var safeBuilder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
            Console.WriteLine(
                $"DB-CONFIG host={safeBuilder.Host}; database={safeBuilder.Database}; username={safeBuilder.Username}; ssl={safeBuilder.SslMode}");

            options.UseNpgsql(connectionString);

            if (environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

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

        // Ã¢â€â‚¬Ã¢â€â‚¬ Repositories Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
        services.AddScoped<IPropertyRepository, PropertyRepository>();
        services.AddScoped<IPropertyImageRepository, PropertyImageRepository>();
        services.AddScoped<IFavoriteRepository, FavoriteRepository>();
        services.AddScoped<IContactMessageRepository, ContactMessageRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();  // Ã¢â€ Â ADDED

        // Ã¢â€â‚¬Ã¢â€â‚¬ Unit of Work Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Ã¢â€â‚¬Ã¢â€â‚¬ Current User Service Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Ã¢â€â‚¬Ã¢â€â‚¬ Cloudinary Media Service Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
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

    // Ã¢â€â‚¬Ã¢â€â‚¬ Connection String Resolver Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬
    private static string ResolveConnectionString(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var rawConnectionString = environment.IsProduction()
            ? Environment.GetEnvironmentVariable("DATABASE_URL")
            : configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(rawConnectionString))
        {
            throw new InvalidOperationException(
                environment.IsProduction()
                    ? "DATABASE_URL environment variable is required in Production."
                    : "ConnectionStrings:DefaultConnection is missing in appsettings.json.");
        }

        return NormalizePostgresConnectionString(rawConnectionString, environment);
    }

    private static string NormalizePostgresConnectionString(
        string rawConnectionString,
        IHostEnvironment environment)
    {
        var value = rawConnectionString.Trim().Trim('"', '\'');

        if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return ConvertPostgresUriToNpgsqlConnectionString(value, environment);
        }

        var builder = new NpgsqlConnectionStringBuilder(value);

        if (environment.IsProduction())
        {
            ValidateProductionDatabaseConnection(builder);
        }

        ApplyDefaultPostgresSettings(builder, environment);

        return builder.ConnectionString;
    }

    private static string ConvertPostgresUriToNpgsqlConnectionString(
        string databaseUrl,
        IHostEnvironment environment)
    {
        if (!Uri.TryCreate(databaseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("DATABASE_URL is not a valid absolute PostgreSQL URI.");
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidOperationException("DATABASE_URL is invalid: host is missing.");
        }

        if (string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            throw new InvalidOperationException("DATABASE_URL is invalid: username and password are missing.");
        }

        var separatorIndex = uri.UserInfo.IndexOf(':');

        if (separatorIndex <= 0 || separatorIndex == uri.UserInfo.Length - 1)
        {
            throw new InvalidOperationException(
                "DATABASE_URL is invalid: expected username:password before host.");
        }

        var rawUsername = uri.UserInfo[..separatorIndex];
        var rawPassword = uri.UserInfo[(separatorIndex + 1)..];

        var username = Uri.UnescapeDataString(rawUsername);
        var password = Uri.UnescapeDataString(rawPassword);
        var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));

        if (string.IsNullOrWhiteSpace(database))
        {
            database = "postgres";
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = database,
            Username = username,
            Password = password
        };

        ApplyDefaultPostgresSettings(builder, environment);

        if (environment.IsProduction())
        {
            ValidateProductionDatabaseConnection(builder);
        }

        return builder.ConnectionString;
    }

    private static void ApplyDefaultPostgresSettings(
        NpgsqlConnectionStringBuilder builder,
        IHostEnvironment environment)
    {
        if (builder.Port == 0)
        {
            builder.Port = 5432;
        }

        builder.Pooling = true;

        if (builder.MaxPoolSize <= 0)
        {
            builder.MaxPoolSize = 20;
        }

        if (builder.Timeout <= 0)
        {
            builder.Timeout = 30;
        }

        if (builder.CommandTimeout <= 0)
        {
            builder.CommandTimeout = 60;
        }

        if (environment.IsProduction())
        {
            builder.SslMode = SslMode.Require;
        }
    }

    private static void ValidateProductionDatabaseConnection(
        NpgsqlConnectionStringBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(builder.Host))
        {
            throw new InvalidOperationException("Production database host is required.");
        }

        if (string.IsNullOrWhiteSpace(builder.Database))
        {
            throw new InvalidOperationException("Production database name is required.");
        }

        if (string.IsNullOrWhiteSpace(builder.Username))
        {
            throw new InvalidOperationException("Production database username is required.");
        }

        if (string.IsNullOrWhiteSpace(builder.Password))
        {
            throw new InvalidOperationException("Production database password is required.");
        }

        if (builder.Host.Contains("pooler.supabase.com", StringComparison.OrdinalIgnoreCase) &&
            !builder.Username.Contains('.', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Supabase pooler username must be in the format postgres.<project-ref>, not postgres.");
        }


        if (builder.SslMode != SslMode.Require &&
            builder.SslMode != SslMode.VerifyFull)
        {
            throw new InvalidOperationException(
                "Production database connection must use SSL Mode=Require or SSL Mode=VerifyFull.");
        }
    }
}




