using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PropertyApi.Application.Auth.Interfaces;

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

    public PhoneAuthWebApplicationFactory()
    {
        TestSecuritySettings.EnsureEnvironmentConfigured();
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
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
    TestSecuritySettings.PhoneLookupHmacKey
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IOtpService>();
            services.AddSingleton<IOtpService, DeterministicOtpService>();

            services.RemoveAll<ISmsService>();
            services.AddSingleton<ISmsService, AlwaysSuccessfulSmsService>();
        });
    }
}
