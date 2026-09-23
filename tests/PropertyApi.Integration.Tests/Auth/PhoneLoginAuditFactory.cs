using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using PropertyApi.Infrastructure.Identity.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Integration.Tests.TestInfrastructure;

namespace PropertyApi.Integration.Tests.Auth;

/// <summary>
/// Real-PostgreSQL host for the phone-login audit. Unlike PhoneAuthWebApplicationFactory (EF
/// in-memory, deterministic OTP), this keeps the production OtpService and the relational code
/// paths of PhoneAuthenticationWorkflow (ExecuteUpdate reservations, unique index), and only
/// replaces the SMS provider with a sink that records the code the real generator produced.
/// Each request may carry X-Test-Ip so tests get their own rate-limit partition.
/// </summary>
public sealed class PhoneLoginAuditFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")
        ?? throw new InvalidOperationException("TEST_POSTGRES_CONNECTION_STRING is required.");

    public CapturingSmsService Sms { get; } = new();
    public CountingPasswordHasher Hasher { get; } = new();

    public bool EnforcementEnabled { get; init; }

    /// <summary>SmsProvider:AllowedCountryCodes for the host (empty = no restriction).</summary>
    public string[] AllowedCountryCodes { get; init; } = [];

    /// <summary>When set, replaces the process clock so expiry and windows can be crossed without waiting.</summary>
    public ManualTimeProvider? Clock { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestSecuritySettings.EnsureEnvironmentConfigured();
        builder.UseEnvironment("Testing");
        builder.UseStableTestLogging();

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Jwt:Issuer"] = TestSecuritySettings.JwtIssuer,
                ["Jwt:Audience"] = TestSecuritySettings.JwtAudience,
                ["Jwt:Key"] = TestSecuritySettings.JwtKey,
                ["OtpSettings:SecretKey"] = TestSecuritySettings.OtpSecretKey,
                ["Security:PhoneLookupHmacKey"] = TestSecuritySettings.PhoneLookupHmacKey,
                ["RateLimiting:Redis:Enabled"] = "false",
                ["PhoneVerification:EnforcementEnabled"] = EnforcementEnabled ? "true" : "false",
            };
            for (var i = 0; i < AllowedCountryCodes.Length; i++)
                settings[$"SmsProvider:AllowedCountryCodes:{i}"] = AllowedCountryCodes[i];
            TestHostConfiguration.AddDataProtectionSettings(settings, nameof(PhoneLoginAuditFactory));
            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.UseEphemeralDataProtection();
            services.RemoveAll<ISmsService>();
            services.AddSingleton<ISmsService>(Sms);
            services.RemoveAll<IPasswordHasher<ApplicationUser>>();
            services.AddSingleton<IPasswordHasher<ApplicationUser>>(Hasher);
            services.AddSingleton<IStartupFilter, TestIpStartupFilter>();
            if (Clock is not null)
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(Clock);
            }
        });
    }

    public async Task PrepareDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        TestHostConfiguration.SeedApplicationRoles(Services);
        await db.Database.ExecuteSqlRawAsync("""DELETE FROM "PhoneOtpChallenges";""");
    }

    private sealed class TestIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue("X-Test-Ip", out var ip) &&
                    System.Net.IPAddress.TryParse(ip.ToString(), out var address))
                    context.Connection.RemoteIpAddress = address;
                await nextMiddleware();
            });
            next(app);
        };
    }
}

/// <summary>A clock the test moves by hand.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private long _offsetTicks;
    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddTicks(Volatile.Read(ref _offsetTicks));
}

/// <summary>Counts password-hasher work so login timing can be asserted without measuring time.</summary>
public sealed class CountingPasswordHasher : PasswordHasher<ApplicationUser>
{
    private int _operations;
    public int Operations => Volatile.Read(ref _operations);

    public override string HashPassword(ApplicationUser user, string password)
    {
        Interlocked.Increment(ref _operations);
        return base.HashPassword(user, password);
    }

    public override PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
    {
        Interlocked.Increment(ref _operations);
        return base.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }
}

public sealed class CapturingSmsService : ISmsService
{
    private readonly ConcurrentDictionary<string, string> _last = new();
    public int SendCount;
    public bool Fail;

    public Task<bool> SendOtpAsync(string phoneNumber, string otp, CancellationToken ct = default)
    {
        Interlocked.Increment(ref SendCount);
        if (Fail) return Task.FromResult(false);
        _last[phoneNumber] = otp;
        return Task.FromResult(true);
    }

    public string? LastCodeFor(string phone) => _last.TryGetValue(phone, out var code) ? code : null;
}
