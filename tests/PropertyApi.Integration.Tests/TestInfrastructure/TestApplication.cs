using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Auth.Entities;
using PropertyApi.Domain.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Integration.Tests.TestInfrastructure;

/// <summary>
/// Shared test host for security and OTP integration tests.
///
/// It boots the real API pipeline and replaces only external infrastructure:
/// - PostgreSQL -> EF Core InMemory database
/// - SMS provider -> deterministic test double
/// - OTP generator -> deterministic test double for repeatable HTTP/DI tests
/// - OTP repository -> in-memory repository with controllable clock
/// </summary>
public sealed class TestApplication : WebApplicationFactory<Program>
{
    private readonly string _environmentName;
    private readonly string _databaseName = $"PropertyApiTests_{Guid.NewGuid():N}";
    private readonly Dictionary<string, string?> _configuration;
    private readonly string? _previousDatabaseUrl;
    private readonly string? _previousForwardedHeadersForwardLimit;
    private readonly string? _previousForwardedHeadersKnownProxy;
    private readonly string? _previousForwardedHeadersKnownNetwork;
    private readonly string? _previousCorsAllowedOrigin;
    private readonly Dictionary<string, string?> _previousStagingEnvironmentVariables = new();

    public TestApplication()
        : this("Testing", null)
    {
    }

    private TestApplication(
        string environmentName = "Testing",
        IDictionary<string, string?>? configurationOverrides = null)
    {
        TestSecuritySettings.EnsureEnvironmentConfigured();
        _environmentName = environmentName;
        _configuration = CreateDefaultConfiguration(environmentName, _databaseName);

        if (configurationOverrides is not null)
        {
            foreach (var pair in configurationOverrides)
            {
                _configuration[pair.Key] = pair.Value;
            }
        }

        _previousDatabaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        _previousForwardedHeadersForwardLimit = Environment.GetEnvironmentVariable("ForwardedHeaders__ForwardLimit");
        _previousForwardedHeadersKnownProxy = Environment.GetEnvironmentVariable("ForwardedHeaders__KnownProxies__0");
        _previousForwardedHeadersKnownNetwork = Environment.GetEnvironmentVariable("ForwardedHeaders__KnownNetworks__0");
        _previousCorsAllowedOrigin = Environment.GetEnvironmentVariable("Cors__AllowedOrigins__0");

        if (string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase))
        {
            Environment.SetEnvironmentVariable(
                "DATABASE_URL",
                "Host=localhost;Port=5432;Database=propertyapi_test;Username=postgres;Password=postgres;SSL Mode=Require;");

            // Program.cs reads ForwardedHeaders immediately after CreateBuilder().
            // WebApplicationFactory.ConfigureAppConfiguration runs too late for that startup guard,
            // therefore these values must exist as environment variables before the host is built.
            Environment.SetEnvironmentVariable("ForwardedHeaders__ForwardLimit", "1");
            Environment.SetEnvironmentVariable("ForwardedHeaders__KnownProxies__0", "203.0.113.1");
            Environment.SetEnvironmentVariable("ForwardedHeaders__KnownNetworks__0", null);

            // Program.cs configures CORS before WebApplicationFactory.ConfigureAppConfiguration
            // is applied, so Production-mode tests must also provide CORS through
            // environment variables before the host starts.
            Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", "https://frontend.example");
        }

        if (string.Equals(environmentName, "Staging", StringComparison.OrdinalIgnoreCase))
        {
            // StagingEnvironmentGuard.Validate runs immediately after CreateBuilder() in
            // Program.cs, before WebApplicationFactory's ConfigureAppConfiguration (the
            // in-memory _configuration collection below) is applied -- same timing gotcha as
            // the ForwardedHeaders/Cors handling above for Production. Every key that guard
            // requires must exist as a real environment variable before the host builds; once
            // set, builder.Configuration's own AddEnvironmentVariables() source keeps exposing
            // them for the rest of the app's lifetime too; nothing needs to also be duplicated
            // into _configuration for CreateStaging callers.
            var requiredStagingEnvironmentVariables = new Dictionary<string, string>
            {
                // StagingEnvironmentGuard.ValidateDatabase reads this too, at the same early
                // point -- CreateDefaultConfiguration's in-memory override of the same key
                // (used by the InMemory-database swap in ConfigureTestServices below) applies
                // too late for the guard, exactly like every other key here. Must contain
                // Staging__DatabaseNameMarker below ("test" is inside "propertyapi_test").
                ["ConnectionStrings__DefaultConnection"] =
                    "Host=localhost;Port=5432;Database=propertyapi_test;Username=postgres;Password=not-used;Trust Server Certificate=true",
                // Same early-guard timing issue (StagingEnvironmentGuard.ValidateRedis) --
                // this only needs to be a non-empty configured value for the guard, since
                // CacheInfrastructureRegistration's AddStackExchangeRedisCache connects lazily
                // rather than at DI-registration time, and nothing in these tests exercises a
                // code path that actually touches the distributed cache.
                ["ConnectionStrings__Redis"] = "localhost:6379",
                // ForwardedHeadersRegistration.AddTrustedForwardedHeaders runs right after
                // StagingEnvironmentGuard.Validate in Program.cs and requires this whenever
                // IsStaging() -- same as the existing Production block below already handles.
                ["ForwardedHeaders__ForwardLimit"] = "1",
                ["ForwardedHeaders__KnownProxies__0"] = "203.0.113.1",
                // CorsRegistration.AddPropertyApiCors also runs before ConfigureAppConfiguration
                // and requires this outside Development -- same as the Production block below.
                ["Cors__AllowedOrigins__0"] = "https://staging-frontend.example.test",
                ["Deployment__CommitSha"] = "0000000000000000000000000000000000000000",
                ["Deployment__Version"] = "test",
                ["Deployment__PublicBaseUrl"] = "https://staging.example.test",
                ["Deployment__ProductionBaseUrl"] = "https://production.example.test",
                ["Staging__EnvironmentId"] = "staging-test",
                ["Staging__DatabaseNameMarker"] = "test",
                ["Staging__RedisIsolationMarker"] = "staging-test",
                ["Staging__StorageIsolationMarker"] = "staging-test",
                ["Staging__ExternalNotificationsDisabled"] = "true",
                ["Staging__TestSupport__Enabled"] = "true",
                ["Staging__TestSupport__FixedOtp"] = "123456",
                ["Staging__TestSupport__PhonePrefix"] = "+15550100",
                ["Staging__TestSupport__CleanupSecret"] = "staging-smoke-test-secret-at-least-32-characters"
            };

            foreach (var pair in requiredStagingEnvironmentVariables)
            {
                _previousStagingEnvironmentVariables[pair.Key] = Environment.GetEnvironmentVariable(pair.Key);
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }

    public static TestApplication CreateProduction(
        IDictionary<string, string?>? configurationOverrides = null)
    {
        return new TestApplication("Production", configurationOverrides);
    }

    /// <summary>
    /// A "Staging" environment host. StagingEnvironmentGuard.Validate runs unconditionally
    /// right after WebApplication.CreateBuilder (Program.cs), so the constructor above sets
    /// every key that guard requires as a real environment variable -- not just test setup
    /// boilerplate, the host fails to start without them. "test" (the DatabaseNameMarker)
    /// intentionally matches the "propertyapi_test" database name CreateDefaultConfiguration
    /// already sets, and CookieCsrf is left at the Testing defaults (disabled) like the base
    /// "Testing" host -- callers that need it layer CookieCsrf:Enabled=true through
    /// configurationOverrides same as CreateTesting callers do.
    /// </summary>
    public static TestApplication CreateStaging(
        IDictionary<string, string?>? configurationOverrides = null)
    {
        return new TestApplication("Staging", configurationOverrides);
    }

    /// <summary>
    /// Same "Testing" environment/InMemory-database host the parameterless constructor
    /// builds, but with extra configuration layered on top -- e.g. CSRF integration tests
    /// that need CookieCsrf:Enabled=true, which appsettings.Testing.json leaves off for the
    /// rest of the suite so that ordinary POST/PUT/DELETE integration tests are not required
    /// to fetch and send a CSRF token to keep passing.
    /// </summary>
    public static TestApplication CreateTesting(
        IDictionary<string, string?>? configurationOverrides = null)
    {
        return new TestApplication("Testing", configurationOverrides);
    }

    /// <summary>Runs <paramref name="action"/> against a fresh DI scope over this fixture's
    /// InMemory database — for seeding fixture data directly (e.g. an Offer/Lead row) or
    /// asserting on persisted state without going through the HTTP pipeline.</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    /// <inheritdoc cref="InScopeAsync{T}"/>
    public async Task InScopeAsync(Func<IServiceProvider, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environmentName);
        builder.UseStableTestLogging();

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(_configuration);
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.UseEphemeralDataProtection();

            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options
                    .UseInMemoryDatabase(_databaseName)
                    .ConfigureWarnings(warnings =>
                        warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));

            services.RemoveAll<IOtpService>();
            services.AddSingleton<IOtpService, DeterministicOtpService>();

            services.RemoveAll<ISmsService>();
            services.AddSingleton<ISmsService, AlwaysSuccessfulSmsService>();

            services.RemoveAll<IRefreshTokenRepository>();
            services.RemoveAll<IRefreshTokenStore>();
            services.AddScoped<EfInMemoryRefreshTokenRepository>();
            services.AddScoped<IRefreshTokenRepository>(sp =>
                sp.GetRequiredService<EfInMemoryRefreshTokenRepository>());
            services.AddScoped<IRefreshTokenStore>(sp =>
                sp.GetRequiredService<EfInMemoryRefreshTokenRepository>());
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        TestHostConfiguration.SeedApplicationRoles(host.Services);

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Environment.SetEnvironmentVariable("DATABASE_URL", _previousDatabaseUrl);
            Environment.SetEnvironmentVariable("ForwardedHeaders__ForwardLimit", _previousForwardedHeadersForwardLimit);
            Environment.SetEnvironmentVariable("ForwardedHeaders__KnownProxies__0", _previousForwardedHeadersKnownProxy);
            Environment.SetEnvironmentVariable("ForwardedHeaders__KnownNetworks__0", _previousForwardedHeadersKnownNetwork);
            Environment.SetEnvironmentVariable("Cors__AllowedOrigins__0", _previousCorsAllowedOrigin);

            foreach (var pair in _previousStagingEnvironmentVariables)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }

        base.Dispose(disposing);
    }

    private static Dictionary<string, string?> CreateDefaultConfiguration(string environmentName, string databaseName)
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=propertyapi_test;Username=postgres;Password=not-used;Trust Server Certificate=true",
            ["ConnectionStrings:Redis"] = "localhost:6379",
            ["Redis:ConnectionString"] = "localhost:6379",

            // CI enables the Redis-backed limiter for the whole test process (see
            // RateLimiting__Redis__Enabled in ci.yml), and every TestApplication-derived host
            // in the suite otherwise shares the same real Redis instance and the same
            // "ip:{loopback}" partition key (RateLimitingRegistration.GetClientRateLimitPartitionKey
            // falls back to the caller's IP for unauthenticated requests, and every TestServer
            // client shares one). Without a per-instance namespace, two IClassFixture<TestApplication>
            // test classes running unauthenticated requests against the same tightly-windowed
            // policy (e.g. "account-delete" = 3/hour, "leads-submit" = 5/hour) exhaust each
            // other's quota and fail with 429 depending on run/class order -- reproduced against
            // AccountDeletionAuthenticationTests and LeadsControllerTests in the same suite run.
            // Same fix as PhoneAuthWebApplicationFactory: give each fixture instance its own key
            // prefix so state never crosses test classes, even on the one shared Redis instance.
            ["RateLimiting:Redis:InstanceName"] = $"PropertyApiTests:TestApplication:{databaseName}:",
            ["Jwt:Issuer"] =
    TestSecuritySettings.JwtIssuer,

            ["Jwt:Audience"] =
    TestSecuritySettings.JwtAudience,

            ["Jwt:Key"] =
    TestSecuritySettings.JwtKey,

            ["Jwt:AccessTokenMinutes"] = "30",
            ["Jwt:RefreshTokenDays"] = "30",

            ["OtpSettings:SecretKey"] =
    TestSecuritySettings.OtpSecretKey,

            ["Security:PhoneLookupHmacKey"] =
    TestSecuritySettings.PhoneLookupHmacKey,
            ["Cloudinary:CloudName"] = "test",
            ["Cloudinary:ApiKey"] = "test",
            ["Cloudinary:ApiSecret"] = "test",
            ["Cors:AllowedOrigins:0"] = "https://frontend.example",
            ["Swagger:Enabled"] = "false",
            ["SmsProvider:Provider"] = "Http",
            ["SmsProvider:ApiUrl"] = "https://sms-provider.example/api/send",
            ["SmsProvider:ApiKey"] = "test-api-key",
            ["SmsProvider:FromNumber"] = "+491234567890",
            ["ForwardedHeaders:ForwardLimit"] = "1",
            ["ForwardedHeaders:KnownProxies:0"] = "203.0.113.1"
        };

        if (!string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase))
        {
            settings["Cors:AllowedOrigins:0"] = "http://localhost:4200";
        }

        TestHostConfiguration.AddDataProtectionSettings(
            settings,
            $"TestApplication_{environmentName}");

        return settings;
    }
}
