using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
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
using PropertyApi.Application.Auth.Contracts;
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
using InfrastructureApplicationRole = PropertyApi.Infrastructure.Identity.Entities.ApplicationRole;
using PropertyApi.Infrastructure.Auth.Services;
using PropertyApi.Infrastructure.Auth;
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
using PropertyApi.Infrastructure.Audit;
using PropertyApi.Infrastructure.Settings;


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

        ConfigureDataProtectionKeyStorage(services, configuration, environment);
        ConfigureDataProtection(services, configuration, environment);

        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddScoped<IIdentityUserService, IdentityUserService>();
        services.AddScoped<IPureIdentityService, PureIdentityService>();
        services.AddScoped<IIdentityRoleService, IdentityRoleService>();
        services.AddScoped<IAuditLogService, AuditLogService>();

        services.AddOptions<SocialAuthSettings>()
            .Bind(configuration.GetSection(SocialAuthSettings.SectionName));

        services.AddMemoryCache();
        services.AddScoped<ISocialTokenVerifier, GoogleTokenVerifier>();
        services.AddHttpClient<AppleTokenVerifier>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddScoped<ISocialTokenVerifier>(sp =>
            sp.GetRequiredService<AppleTokenVerifier>());

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

        // Email
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

        // OTP / SMS Auth Services
        services.AddOptions<SmsProviderOptions>()
            .Bind(configuration.GetSection(SmsProviderOptions.SectionName));

        if (environment.IsProduction())
        {
            services.AddOptions<SmsProviderOptions>()
                .Bind(configuration.GetSection(SmsProviderOptions.SectionName))
                .Validate(options => !string.IsNullOrWhiteSpace(options.Provider), "SmsProvider:Provider is required in Production.")
                .Validate(options => !string.IsNullOrWhiteSpace(options.ApiUrl), "SmsProvider:ApiUrl is required in Production.")
                .Validate(options => Uri.TryCreate(options.ApiUrl, UriKind.Absolute, out var uri) &&
                                     uri.Scheme == Uri.UriSchemeHttps,
                    "SmsProvider:ApiUrl must be a valid absolute HTTPS URL in Production.")
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
            var smsProvider = configuration["SmsProvider:Provider"];

            if (string.Equals(smsProvider, "Twilio", StringComparison.OrdinalIgnoreCase))
            {
                services.AddScoped<ISmsService, TwilioSmsService>();
            }
            else
            {
                services.AddHttpClient<ISmsService, HttpSmsService>();
            }
        }

        services.AddDbContext<AppDbContext>(options =>
        {
            var connectionString = ResolveConnectionString(configuration, environment);

            // ✅ إصلاح H-1: حُذف Console.WriteLine الذي كان يُسرِّب بيانات الاتصال بـ DB.
            //
            // المشكلة القديمة: كان يطبع Host + Database + Username في stdout، مما يجعلها
            // مرئية في Azure Log Stream وأي Log Aggregator ويساعد المهاجم في تحديد هدفه.
            //
            // القاعدة الذهبية: لا تُسجِّل أي بيانات اتصال في Startup.
            // إذا أردت التحقق من الاتصال، استخدم Health Check /health بدلاً من ذلك.
            //
            // إذا كنت بحاجة لـ Logging أمين هنا، استخدم فقط:
            //   logger.LogInformation("DB connected: Host={Host}", safeBuilder.Host);
            // ولا تُسجِّل Username أو Database Name أبداً.

            options.UseNpgsql(connectionString);

            if (environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        services.AddIdentity<User, InfrastructureApplicationRole>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = true;
            // Phone-only accounts are valid in this application and start without
            // an email address. Email uniqueness is enforced explicitly in the
            // email registration/add-email flows and by the database unique index.
            options.User.RequireUniqueEmail = false;
        })
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        // Repositories
        services.AddScoped<IPropertyRepository, PropertyRepository>();
        services.AddScoped<IPropertyImageRepository, PropertyImageRepository>();
        services.AddScoped<IFavoriteRepository, FavoriteRepository>();
        services.AddScoped<IContactMessageRepository, ContactMessageRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();  // ADDED

        // Unit of Work
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Current User Service
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Cloudinary Media Service
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


    private static void ConfigureDataProtectionKeyStorage(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (!ShouldPersistDataProtectionKeysToDatabase(configuration, environment))
        {
            return;
        }

        services.AddDbContext<DataProtectionKeyDbContext>(options =>
        {
            var connectionString = ResolveConnectionString(configuration, environment);
            options.UseNpgsql(connectionString);
        });
    }

    private static void ConfigureDataProtection(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var keysPath = configuration["DataProtection:KeysPath"]
            ?? Environment.GetEnvironmentVariable("DATA_PROTECTION_KEYS_PATH");

        var builder = services.AddDataProtection()
            .SetApplicationName("PropertyApi");

        if (ShouldPersistDataProtectionKeysToDatabase(configuration, environment))
        {
            builder.PersistKeysToDbContext<DataProtectionKeyDbContext>();
            return;
        }

        if (!string.IsNullOrWhiteSpace(keysPath))
        {
            var directory = new DirectoryInfo(keysPath);
            directory.Create();
            builder.PersistKeysToFileSystem(directory);
            return;
        }

        if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "Data Protection key persistence is required in Production. " +
                "Set DataProtection:PersistKeysToDatabase=true or configure DATA_PROTECTION_KEYS_PATH/DataProtection:KeysPath.");
        }
    }

    private static bool ShouldPersistDataProtectionKeysToDatabase(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var configuredValue = configuration["DataProtection:PersistKeysToDatabase"]
            ?? Environment.GetEnvironmentVariable("DATA_PROTECTION_PERSIST_KEYS_TO_DATABASE");

        if (bool.TryParse(configuredValue, out var configured))
        {
            return configured;
        }

        // Production on Render/Supabase should not depend on a local filesystem.
        // Persisting to PostgreSQL keeps the key ring stable across redeploys.
        return environment.IsProduction();
    }

    // Connection String Resolver
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

        NpgsqlConnectionStringBuilder builder;

        if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            builder = ConvertPostgresUriToNpgsqlBuilder(value);
        }
        else
        {
            builder = new NpgsqlConnectionStringBuilder(value);
        }

        ApplyDefaultPostgresSettings(builder, environment);

        if (environment.IsProduction())
        {
            ValidateProductionDatabaseConnection(builder);
        }

        return builder.ConnectionString;
    }

    private static NpgsqlConnectionStringBuilder ConvertPostgresUriToNpgsqlBuilder(
    string postgresUri)
    {
        var uri = new Uri(postgresUri);

        var userInfoParts = uri.UserInfo.Split(':', 2);

        if (userInfoParts.Length != 2)
        {
            throw new InvalidOperationException(
                "Postgres URI must contain both username and password.");
        }

        var username = Uri.UnescapeDataString(userInfoParts[0]);
        var password = Uri.UnescapeDataString(userInfoParts[1]);
        var database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = string.IsNullOrWhiteSpace(database) ? "postgres" : database,
            Username = username,
            Password = password
        };

        var query = uri.Query.TrimStart('?');

        if (!string.IsNullOrWhiteSpace(query))
        {
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var keyValue = part.Split('=', 2);
                if (keyValue.Length != 2)
                {
                    continue;
                }

                var key = Uri.UnescapeDataString(keyValue[0]);
                var value = Uri.UnescapeDataString(keyValue[1]);

                if (key.Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                {
                    builder.SslMode = value.Equals("require", StringComparison.OrdinalIgnoreCase)
                        ? SslMode.Require
                        : Enum.Parse<SslMode>(value, ignoreCase: true);
                }
            }
        }

        return builder;
    }

    private static void ApplyDefaultPostgresSettings(
    NpgsqlConnectionStringBuilder builder,
    IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var isSupabasePooler =
            !string.IsNullOrWhiteSpace(builder.Host) &&
            builder.Host.Contains(".pooler.supabase.com", StringComparison.OrdinalIgnoreCase);

        // Npgsql default port is 5432.
        // لا نفرض 6543 لأن Supabase Pooler لديه:
        // - Session mode: 5432
        // - Transaction mode: 6543
        if (builder.Port <= 0)
        {
            builder.Port = 5432;
        }

        builder.Pooling = true;

        // Npgsql default Maximum Pool Size قد يكون 100.
        // هذا كبير نسبيًا لمشروع صغير على Render/Supabase.
        if (isSupabasePooler && builder.MaxPoolSize > 20)
        {
            builder.MaxPoolSize = 20;
        }

        if (builder.MaxPoolSize <= 0)
        {
            builder.MaxPoolSize = isSupabasePooler ? 20 : 50;
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

            // لا تستخدم builder.Encrypt هنا؛ غير موجود في Npgsql.
            // TrustServerCertificate في Npgsql 8 أصبح غير ضروري/obsolete.
            // اتركه بدون ضبط إلا إذا كنت تستخدم إصدارًا قديمًا وتعرف السبب.
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
        if (HasEnabledTrustServerCertificate(builder))
        {
            throw new InvalidOperationException(
                "Production database connection must not contain Trust Server Certificate=true. Use SSL Mode=Require or SSL Mode=VerifyFull without trusting invalid server certificates.");
        }

        if (builder.Password.Contains("[YOUR-PASSWORD]", StringComparison.OrdinalIgnoreCase) ||
            builder.Password.Contains("YOUR_SUPABASE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Production database password still contains a placeholder value.");
        }

        var isSupabasePooler =
            builder.Host.Contains(".pooler.supabase.com", StringComparison.OrdinalIgnoreCase);

        var isSupabaseDirect =
            builder.Host.StartsWith("db.", StringComparison.OrdinalIgnoreCase) &&
            builder.Host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase);

        if (isSupabasePooler)
        {
            if (!builder.Username.Contains('.', StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Supabase Pooler username must be in the format '<db-user>.<project-ref>', for example 'postgres.wevfsshyxmydfiifjzka'.");
            }

            var usernameParts = builder.Username.Split('.', 2);

            if (usernameParts.Length != 2 ||
                string.IsNullOrWhiteSpace(usernameParts[0]) ||
                string.IsNullOrWhiteSpace(usernameParts[1]))
            {
                throw new InvalidOperationException(
                    "Invalid Supabase Pooler username. Expected '<db-user>.<project-ref>'.");
            }

            if (builder.Port != 5432 && builder.Port != 6543)
            {
                throw new InvalidOperationException(
                    "Supabase Pooler port must be 5432 for Session mode or 6543 for Transaction mode.");
            }
        }

        if (isSupabaseDirect)
        {
            if (builder.Username.Contains('.', StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Supabase direct database connection usually uses username 'postgres', not 'postgres.<project-ref>'. The '<project-ref>' format is for Pooler.");
            }

            if (builder.Port != 5432)
            {
                throw new InvalidOperationException(
                    "Supabase direct database connection should use port 5432.");
            }
        }

        if (builder.SslMode != SslMode.Require &&
            builder.SslMode != SslMode.VerifyFull)
        {
            throw new InvalidOperationException(
                "Production database connection must use SSL Mode=Require or SSL Mode=VerifyFull.");
        }
    }
    private static bool HasEnabledTrustServerCertificate(NpgsqlConnectionStringBuilder builder)
    {
        var hasSpacedAlias =
            builder.TryGetValue("Trust Server Certificate", out var spacedAliasValue) &&
            IsEnabledBooleanConnectionStringValue(spacedAliasValue);

        var hasCompactAlias =
            builder.TryGetValue("TrustServerCertificate", out var compactAliasValue) &&
            IsEnabledBooleanConnectionStringValue(compactAliasValue);

        return hasSpacedAlias || hasCompactAlias;
    }

    private static bool IsEnabledBooleanConnectionStringValue(object? value)
    {
        return value switch
        {
            bool boolValue => boolValue,
            string stringValue => bool.TryParse(stringValue, out var boolValue) && boolValue,
            _ => false
        };
    }
}

