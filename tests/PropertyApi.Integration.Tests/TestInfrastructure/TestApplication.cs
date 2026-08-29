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
        _configuration = CreateDefaultConfiguration(environmentName);

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
    }

    public static TestApplication CreateProduction(
        IDictionary<string, string?>? configurationOverrides = null)
    {
        return new TestApplication("Production", configurationOverrides);
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
        }

        base.Dispose(disposing);
    }

    private static Dictionary<string, string?> CreateDefaultConfiguration(string environmentName)
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=propertyapi_test;Username=postgres;Password=not-used;Trust Server Certificate=true",
            ["ConnectionStrings:Redis"] = "localhost:6379",
            ["Redis:ConnectionString"] = "localhost:6379",
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
