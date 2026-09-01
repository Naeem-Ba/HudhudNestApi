using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Integration.Tests.TestInfrastructure;

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
        // BUG-28: AddRateLimiter was moved out of Program.cs and into its own extension
        // method (RegisterRateLimiting -> AddPropertyApiRateLimiting) in
        // Configuration/RateLimitingRegistration.cs during the Short-Stay Accommodation
        // work (b41833a). This test was never updated, so it kept reading Program.cs --
        // which calls the extension method but no longer contains "AddRateLimiter(" at
        // all -- and found zero matches instead of the real registration.
        var programSource = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "PropertyApi", "Program.cs"));

        Assert.Contains("AddPropertyApiRateLimiting", programSource);

        var registrationSource = File.ReadAllText(
            Path.Combine(FindRepositoryRoot(), "PropertyApi", "Configuration", "RateLimitingRegistration.cs"));

        var addRateLimiterMatches = Regex.Matches(registrationSource, @"AddRateLimiter\s*\(");
        Assert.Single(addRateLimiterMatches);

        var rateLimiterBlock = ExtractRateLimiterBlock(registrationSource);

        var policyNames = Regex
            .Matches(rateLimiterBlock, @"\.AddPolicy\(\s*""([^""]+)""")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.NotEmpty(policyNames);

        var duplicatePolicyNames = policyNames
            .GroupBy(name => name)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.True(
            duplicatePolicyNames.Length == 0,
            $"Duplicate rate-limit policies found: {string.Join(", ", duplicatePolicyNames)}");

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
        const string marker = "services.AddRateLimiter";

        var startIndex = programSource.IndexOf(marker, StringComparison.Ordinal);

        Assert.True(
            startIndex >= 0,
            "Could not find the AddRateLimiter registration in RateLimitingRegistration.cs.");

        var openParenIndex = programSource.IndexOf('(', startIndex);

        Assert.True(
            openParenIndex >= 0,
            "Could not determine the start of the AddRateLimiter registration block.");

        var depth = 0;
        var inString = false;
        var inVerbatimString = false;
        var inChar = false;
        var inSingleLineComment = false;
        var inMultiLineComment = false;

        for (var i = openParenIndex; i < programSource.Length; i++)
        {
            var current = programSource[i];
            var next = i + 1 < programSource.Length ? programSource[i + 1] : '\0';

            if (inSingleLineComment)
            {
                if (current is '\r' or '\n')
                {
                    inSingleLineComment = false;
                }

                continue;
            }

            if (inMultiLineComment)
            {
                if (current == '*' && next == '/')
                {
                    inMultiLineComment = false;
                    i++;
                }

                continue;
            }

            if (!inString && !inChar)
            {
                if (current == '/' && next == '/')
                {
                    inSingleLineComment = true;
                    i++;
                    continue;
                }

                if (current == '/' && next == '*')
                {
                    inMultiLineComment = true;
                    i++;
                    continue;
                }
            }

            if (!inChar && current == '"' && !inString)
            {
                inString = true;
                inVerbatimString = i > 0 && programSource[i - 1] == '@';
                continue;
            }

            if (inString)
            {
                if (inVerbatimString)
                {
                    if (current == '"' && next == '"')
                    {
                        i++;
                        continue;
                    }

                    if (current == '"')
                    {
                        inString = false;
                        inVerbatimString = false;
                    }

                    continue;
                }

                if (current == '\\')
                {
                    i++;
                    continue;
                }

                if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (!inString && current == '\'' && !inChar)
            {
                inChar = true;
                continue;
            }

            if (inChar)
            {
                if (current == '\\')
                {
                    i++;
                    continue;
                }

                if (current == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            if (current == '(')
            {
                depth++;
                continue;
            }

            if (current == ')')
            {
                depth--;

                if (depth == 0)
                {
                    var semicolonIndex = programSource.IndexOf(';', i);

                    Assert.True(
                        semicolonIndex >= 0,
                        "Could not determine the end of the AddRateLimiter registration block.");

                    return programSource[startIndex..(semicolonIndex + 1)];
                }
            }
        }

        Assert.Fail("Could not determine the end of the AddRateLimiter registration block.");

        return string.Empty;
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
        private readonly string _databaseName = $"PropertyApiStartupTests_{Guid.NewGuid():N}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseStableTestLogging();

            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Port=5432;Database=propertyapi_testing;Username=postgres;Password=postgres;Trust Server Certificate=true",
                    ["Database:ApplyMigrationsOnStartup"] = "false",
                    ["Database:SeedOnStartup"] = "false",
                    ["RateLimiting:Redis:Enabled"] = "false",
                    ["RedisRateLimiting:Enabled"] = "false",
                    ["Jwt:Issuer"] =
    PropertyApi.Integration.Tests.TestInfrastructure
        .TestSecuritySettings.JwtIssuer,

                    ["Jwt:Audience"] =
    PropertyApi.Integration.Tests.TestInfrastructure
        .TestSecuritySettings.JwtAudience,

                    ["Jwt:Key"] =
    PropertyApi.Integration.Tests.TestInfrastructure
        .TestSecuritySettings.JwtKey,

                    ["OtpSettings:SecretKey"] =
    PropertyApi.Integration.Tests.TestInfrastructure
        .TestSecuritySettings.OtpSecretKey,

                    ["Security:PhoneLookupHmacKey"] =
    PropertyApi.Integration.Tests.TestInfrastructure
        .TestSecuritySettings.PhoneLookupHmacKey,
                    ["Cloudinary:CloudName"] = "test",
                    ["Cloudinary:ApiKey"] = "test",
                    ["Cloudinary:ApiSecret"] = "test",
                    ["Cors:AllowedOrigins:0"] = "http://localhost:4200",
                    ["Swagger:Enabled"] = "false"
                };

                TestHostConfiguration.AddDataProtectionSettings(
                    settings,
                    nameof(StartupWebApplicationFactory));

                config.AddInMemoryCollection(settings);
            });

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.UseEphemeralDataProtection();

                services.AddDbContext<AppDbContext>(options =>
                    options
                        .UseInMemoryDatabase(_databaseName)
                        .ConfigureWarnings(warnings =>
                            warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }
    }
}
