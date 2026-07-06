using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PropertyApi.Application.Auth.Interfaces;
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
    private readonly Dictionary<string, string?> _configuration;
    private readonly string? _previousDatabaseUrl;
    private readonly string? _previousForwardedHeadersForwardLimit;
    private readonly string? _previousForwardedHeadersKnownProxy;
    private readonly string? _previousForwardedHeadersKnownNetwork;
    private readonly string? _previousCorsAllowedOrigin;

    public TestApplication(
        string environmentName = "Testing",
        IDictionary<string, string?>? configurationOverrides = null)
    {
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environmentName);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(_configuration);
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();

            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase($"PropertyApiTests_{Guid.NewGuid():N}"));

            services.RemoveAll<IOtpService>();
            services.AddSingleton<IOtpService, DeterministicOtpService>();

            services.RemoveAll<ISmsService>();
            services.AddSingleton<ISmsService, AlwaysSuccessfulSmsService>();

            services.RemoveAll<IOtpCodeRepository>();
            services.AddSingleton<InMemoryOtpCodeRepository>();
            services.AddSingleton<IOtpCodeRepository>(sp =>
                sp.GetRequiredService<InMemoryOtpCodeRepository>());
        });
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
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=propertyapi_test;Username=postgres;Password=postgres;Trust Server Certificate=true",
            ["ConnectionStrings:Redis"] = "localhost:6379",
            ["Redis:ConnectionString"] = "localhost:6379",
            ["Jwt:Issuer"] = "PropertyApi",
            ["Jwt:Audience"] = "PropertyApiClient",
            ["Jwt:Key"] = "TEST_ONLY_SECRET_KEY_1234567890_1234567890",
            ["Jwt:AccessTokenMinutes"] = "30",
            ["Jwt:RefreshTokenDays"] = "30",
            ["OtpSettings:SecretKey"] = "TEST_ONLY_OTP_SECRET_KEY_1234567890_1234567890",
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

        return settings;
    }
}

public sealed class InMemoryOtpCodeRepository : IOtpCodeRepository
{
    private readonly List<OtpCode> _items = new();
    private readonly object _gate = new();
    private TimeSpan _clockOffset = TimeSpan.Zero;

    private DateTime UtcNow => DateTime.UtcNow.Add(_clockOffset);

    public Task AddAsync(OtpCode otpCode, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _items.Add(otpCode);
        }

        return Task.CompletedTask;
    }

    public Task<OtpCode?> GetLatestValidAsync(
        string phoneNumber,
        OtpPurpose purpose,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            DeleteExpiredUnsafe(UtcNow);

            var item = _items
                .Where(o =>
                    o.PhoneNumber == phoneNumber &&
                    o.Purpose == purpose &&
                    o.ExpiresAt > UtcNow &&
                    !o.IsUsed &&
                    o.AttemptCount < 3)
                .OrderByDescending(o => o.CreatedAt)
                .FirstOrDefault();

            return Task.FromResult(item);
        }
    }

    public Task<int> CountRecentAsync(
        string phoneNumber,
        TimeSpan window,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            DeleteExpiredUnsafe(UtcNow);
            var cutoff = UtcNow - window;

            var count = _items.Count(o =>
                o.PhoneNumber == phoneNumber &&
                o.CreatedAt >= cutoff);

            return Task.FromResult(count);
        }
    }

    public Task<int> DeleteExpiredAsync(
        DateTime utcNow,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(DeleteExpiredUnsafe(utcNow));
        }
    }

    public Task<bool> TryConsumeAsync(
        Guid otpCodeId,
        DateTime utcNow,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            var otpCode = _items.FirstOrDefault(o =>
                o.Id == otpCodeId &&
                !o.IsUsed &&
                o.ExpiresAt > utcNow &&
                o.AttemptCount < 3);

            if (otpCode is null)
                return Task.FromResult(false);

            otpCode.MarkAsUsed();
            return Task.FromResult(true);
        }
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
        => Task.CompletedTask;

    public void AdvanceTimeBy(TimeSpan offset)
    {
        lock (_gate)
        {
            _clockOffset = _clockOffset.Add(offset);
        }
    }

    public void ResetTime()
    {
        lock (_gate)
        {
            _clockOffset = TimeSpan.Zero;
        }
    }

    private int DeleteExpiredUnsafe(DateTime utcNow)
    {
        return _items.RemoveAll(o => o.ExpiresAt <= utcNow);
    }
}
