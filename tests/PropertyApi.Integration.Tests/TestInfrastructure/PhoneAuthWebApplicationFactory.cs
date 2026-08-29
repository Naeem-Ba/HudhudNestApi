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
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Integration.Tests.TestInfrastructure;

/// <summary>
/// Test host for the Phone Auth integration flow.
///
/// It keeps the real API pipeline, controllers, MediatR, EF Core, Identity,
/// refresh-token persistence, and routing, but replaces external OTP/SMS
/// dependencies with deterministic test doubles.
/// </summary>
public sealed class PhoneAuthWebApplicationFactory
    : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"PropertyApiPhoneAuthTests_{Guid.NewGuid():N}";

    public PhoneAuthWebApplicationFactory()
    {
        TestSecuritySettings.EnsureEnvironmentConfigured();
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseStableTestLogging();

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] =
    TestSecuritySettings.JwtIssuer,

                ["Jwt:Audience"] =
    TestSecuritySettings.JwtAudience,

                ["Jwt:Key"] =
    TestSecuritySettings.JwtKey,

                ["OtpSettings:SecretKey"] =
    TestSecuritySettings.OtpSecretKey,

                ["Security:PhoneLookupHmacKey"] =
    TestSecuritySettings.PhoneLookupHmacKey,

                // CI enables the Redis-backed limiter for the whole test process.
                // Give this host its own key namespace so another factory cannot
                // exhaust auth-login for TestServer's shared "unknown" client IP.
                ["RateLimiting:Redis:InstanceName"] =
    $"PropertyApiTests:PhoneAuth:{_databaseName}:"
            };

            TestHostConfiguration.AddDataProtectionSettings(
                settings,
                nameof(PhoneAuthWebApplicationFactory));

            config.AddInMemoryCollection(settings);
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
}
