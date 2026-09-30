using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HudhudNestApi.Integration.Tests.TestInfrastructure;

/// <summary>
/// Test host for SignalR notification integration tests.
///
/// It uses the real API pipeline, real PostgreSQL test database, real EF Core,
/// real Identity/JWT validation, real NotificationService, and real
/// NotificationHub.
/// </summary>
public sealed class NotificationWebApplicationFactory
    : WebApplicationFactory<Program>
{
    private readonly string _instanceId = Guid.NewGuid().ToString("N");

    public NotificationWebApplicationFactory()
    {
        TestSecuritySettings.EnsureEnvironmentConfigured();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseStableTestLogging();

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var connectionString =
                Environment.GetEnvironmentVariable(
                    "TEST_POSTGRES_CONNECTION_STRING")
                ?? Environment.GetEnvironmentVariable(
                    "ConnectionStrings__DefaultConnection")
                ?? throw new InvalidOperationException(
                    "A PostgreSQL test connection string is required. " +
                    "Set TEST_POSTGRES_CONNECTION_STRING or " +
                    "ConnectionStrings__DefaultConnection.");

            var settings =
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        connectionString,

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

                    ["Cors:AllowedOrigins:0"] =
                        "http://localhost:4200",

                    // CI enables the Redis-backed limiter for the whole test process, and every
                    // TestApplication-derived host in the suite otherwise shares the same real
                    // Redis instance/key namespace and the same "ip:{loopback}" partition (see
                    // TestApplication's CreateDefaultConfiguration for the full rationale and the
                    // cross-class 429 flakiness this fixes).
                    ["RateLimiting:Redis:InstanceName"] =
                        $"HudhudNestApiTests:NotificationWebApplicationFactory:{_instanceId}:"
                };

            TestHostConfiguration.AddDataProtectionSettings(
                settings,
                nameof(NotificationWebApplicationFactory));

            config.AddInMemoryCollection(settings);
        });
    }
}
