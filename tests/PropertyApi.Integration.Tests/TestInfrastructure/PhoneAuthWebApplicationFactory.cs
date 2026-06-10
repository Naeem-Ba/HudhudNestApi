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
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "PropertyApi",
                ["Jwt:Audience"] = "PropertyApiClient",
                ["Jwt:Key"] = "TEST_ONLY_SECRET_KEY_1234567890_1234567890",
                ["OtpSettings:SecretKey"] = "TEST_ONLY_OTP_SECRET_KEY_1234567890_1234567890"
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
