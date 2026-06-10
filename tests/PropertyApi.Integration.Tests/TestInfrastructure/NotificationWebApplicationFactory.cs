using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace PropertyApi.Integration.Tests.TestInfrastructure;

/// <summary>
/// Test host for SignalR notification integration tests.
///
/// It uses the real API pipeline, real PostgreSQL test database, real EF Core,
/// real Identity/JWT validation, real NotificationService, and real NotificationHub.
/// External services are not mocked here because the notification flow does not need SMS/Email/Cloudinary.
/// </summary>
public sealed class NotificationWebApplicationFactory
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
                ?? "Host=localhost;Port=5432;Database=propertyapi_testing;Username=postgres;Password=27052017;Trust Server Certificate=true";

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Jwt:Issuer"] = "PropertyApi",
                ["Jwt:Audience"] = "PropertyApiClient",
                ["Jwt:Key"] = "TEST_ONLY_SECRET_KEY_1234567890_1234567890",
                ["Jwt:AccessTokenMinutes"] = "30",
                ["Jwt:RefreshTokenDays"] = "30",
                ["OtpSettings:SecretKey"] = "TEST_ONLY_OTP_SECRET_KEY_1234567890_1234567890",
                ["Cloudinary:CloudName"] = "test",
                ["Cloudinary:ApiKey"] = "test",
                ["Cloudinary:ApiSecret"] = "test",
                ["Cors:AllowedOrigins:0"] = "http://localhost:4200"
            });
        });
    }
}
