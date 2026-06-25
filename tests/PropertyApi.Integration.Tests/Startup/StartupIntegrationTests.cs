using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Integration.Tests.Startup;

/// <summary>
/// Startup smoke tests for critical application bootstrapping.
///
/// These tests intentionally build the real API host. They catch registration-time
/// failures such as duplicate rate-limiter policy names before the application is deployed.
/// External infrastructure is replaced only where needed to keep the startup check deterministic.
/// </summary>
public sealed class StartupIntegrationTests : IAsyncLifetime
{
    private StartupWebApplicationFactory? _factory;
    private HttpClient? _client;

    public Task InitializeAsync()
    {
        _factory = new StartupWebApplicationFactory();
        _client = _factory.CreateClient();

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();

        return Task.CompletedTask;
    }

    [Fact(DisplayName = "Application host starts and serves a public API endpoint")]
    [Trait("Category", "Integration")]
    [Trait("Feature", "Startup")]
    public async Task Application_Should_Start_And_Serve_Public_Endpoint()
    {
        Assert.NotNull(_client);

        var response = await _client!.GetAsync("/api/enums/PropertyStatus");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Expected startup smoke endpoint to return 200 OK, but got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
    }

    [Fact(DisplayName = "Program.cs has one AddRateLimiter registration and no duplicate rate-limit policies")]
    [Trait("Category", "Architecture")]
    [Trait("Feature", "RateLimiting")]
    public void Program_Should_Register_RateLimiter_Once_Without_Duplicate_Policy_Names()
    {
        var programSource = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "PropertyApi", "Program.cs"));

        Assert.Equal(1, Regex.Matches(programSource, @"AddRateLimiter\s*\(").Count);

        var rateLimiterBlock = ExtractRateLimiterBlock(programSource);
        var policyNames = Regex.Matches(
                rateLimiterBlock,
                @"Add(?:Policy|FixedWindowLimiter)\s*\(\s*""([^""]+)""")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        var duplicatePolicyNames = policyNames
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicatePolicyNames);

        Assert.Contains("send-otp", policyNames);
        Assert.Contains("verify-otp", policyNames);
        Assert.Contains("auth-password-reset", policyNames);
        Assert.Contains("contact", policyNames);
        Assert.Contains("auth-login", policyNames);
        Assert.Contains("auth-register", policyNames);
        Assert.Contains("visits", policyNames);
        Assert.Contains("reviews", policyNames);
    }

    private static string ExtractRateLimiterBlock(string programSource)
    {
        var start = programSource.IndexOf(
            "builder.Services.AddRateLimiter(options =>",
            StringComparison.Ordinal);

        Assert.True(start >= 0, "Program.cs does not contain builder.Services.AddRateLimiter(...).");

        var end = programSource.IndexOf(
            "// --------------------------------------------------------------",
            start,
            StringComparison.Ordinal);

        Assert.True(end > start, "Could not determine the end of the AddRateLimiter registration block.");

        return programSource[start..end];
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PropertyApi.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root containing PropertyApi.sln.");
    }

    private sealed class StartupWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=propertyapi_testing;Username=postgres;Password=27052017;Trust Server Certificate=true",
                    ["Jwt:Issuer"] = "PropertyApi",
                    ["Jwt:Audience"] = "PropertyApiClient",
                    ["Jwt:Key"] = "TEST_ONLY_SECRET_KEY_1234567890_1234567890",
                    ["Jwt:AccessTokenMinutes"] = "30",
                    ["Jwt:RefreshTokenDays"] = "30",
                    ["OtpSettings:SecretKey"] = "TEST_ONLY_OTP_SECRET_KEY_1234567890_1234567890",
                    ["Cloudinary:CloudName"] = "test",
                    ["Cloudinary:ApiKey"] = "test",
                    ["Cloudinary:ApiSecret"] = "test",
                    ["Cors:AllowedOrigins:0"] = "http://localhost:4200",
                    ["Swagger:Enabled"] = "false"
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();

                services.AddDbContext<AppDbContext>(options =>
                    options.UseInMemoryDatabase($"PropertyApiStartupTests_{Guid.NewGuid():N}"));
            });
        }
    }
}
