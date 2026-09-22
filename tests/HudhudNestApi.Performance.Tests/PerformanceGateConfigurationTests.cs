using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using HudhudNestApi.Infrastructure.Performance;
using HudhudNestApi.Performance;
using Xunit;

namespace HudhudNestApi.Performance.Tests;

public sealed class PerformanceGateConfigurationTests
{
    [Fact]
    public void Production_never_exposes_instance_or_database_diagnostics()
    {
        var environment = new TestHostEnvironment(Environments.Production);
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Performance:ExposeInstanceId"] = "true",
            ["Performance:ExposeDatabaseDiagnostics"] = "true",
            ["Performance:InstanceId"] = "api-1"
        });

        Assert.False(PerformanceInstanceHeaderPolicy.IsEnabled(environment, configuration));
        Assert.False(PerformanceDatabaseDiagnosticsPolicy.IsEnabled(environment, configuration));
    }

    [Fact]
    public void Staging_exposes_diagnostics_only_when_explicitly_enabled()
    {
        var environment = new TestHostEnvironment(Environments.Staging);
        var enabled = Configuration(new Dictionary<string, string?>
        {
            ["Performance:ExposeInstanceId"] = "true",
            ["Performance:ExposeDatabaseDiagnostics"] = "true",
            ["Performance:InstanceId"] = "api-2"
        });
        var disabled = Configuration(new Dictionary<string, string?>());

        Assert.True(PerformanceInstanceHeaderPolicy.IsEnabled(environment, enabled));
        Assert.True(PerformanceDatabaseDiagnosticsPolicy.IsEnabled(environment, enabled));
        Assert.Equal("api-2", PerformanceInstanceHeaderPolicy.GetValidatedInstanceId(enabled));
        Assert.False(PerformanceInstanceHeaderPolicy.IsEnabled(environment, disabled));
        Assert.False(PerformanceDatabaseDiagnosticsPolicy.IsEnabled(environment, disabled));
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains spaces")]
    [InlineData("contains/slash")]
    public void Unsafe_instance_identifiers_are_rejected(string instanceId)
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Performance:InstanceId"] = instanceId
        });
        Assert.Throws<InvalidOperationException>(() =>
            PerformanceInstanceHeaderPolicy.GetValidatedInstanceId(configuration));
    }

    [Fact]
    public void Budgets_define_every_release_blocking_metric_and_are_backed_by_an_approved_baseline()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Repo("performance", "performance-budgets.json")));
        var root = document.RootElement;
        Assert.True(root.GetProperty("approved").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("approval").GetProperty("approvedBy").GetString()));
        Assert.True(File.Exists(Repo("performance", "baselines", "approved-baseline.json")));
        Assert.Equal(2, root.GetProperty("environment").GetProperty("minimumApiInstances").GetInt32());
        Assert.True(root.GetProperty("environment").GetProperty("minimumDatasetSize").GetInt32() >= 10_000);
        foreach (var scenarioName in new[] { "propertyList", "propertyDetail", "geographicSearch" })
        {
            var scenario = root.GetProperty("scenarios").GetProperty(scenarioName);
            Assert.True(scenario.GetProperty("p95Milliseconds").GetInt32() > 0);
            Assert.True(scenario.GetProperty("p99Milliseconds").GetInt32() > 0);
            Assert.True(scenario.GetProperty("minimumRequestsPerSecond").GetInt32() > 0);
            Assert.True(scenario.GetProperty("maximumQueryCount").GetInt32() > 0);
        }
    }

    [Fact]
    public void Compose_and_k6_define_two_instances_concurrency_and_database_evidence()
    {
        var compose = File.ReadAllText(Repo("performance", "docker-compose.performance.yml"));
        var browse = File.ReadAllText(Repo("performance", "load-tests", "scenarios", "browse.js"));
        var workflow = File.ReadAllText(Repo(".github", "workflows", "performance-validation.yml"));

        Assert.Contains("api1:", compose, StringComparison.Ordinal);
        Assert.Contains("api2:", compose, StringComparison.Ordinal);
        Assert.Contains("shared_preload_libraries=pg_stat_statements", compose, StringComparison.Ordinal);
        Assert.Contains("constant-vus", browse, StringComparison.Ordinal);
        Assert.Contains("p(95)", browse, StringComparison.Ordinal);
        Assert.Contains("p(99)", browse, StringComparison.Ordinal);
        Assert.Contains("run-performance-tests.sh", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("continue-on-error", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void K6_contracts_use_the_APIs_configured_camelCase_JSON_names()
    {
        // BUG-23: Program.cs has used JsonNamingPolicy.CamelCase since the B-11
        // migration. This test (and, until fixed alongside it, the k6 scripts
        // themselves) still asserted the pre-B-11 PascalCase contract, which no
        // longer exists on the wire — every affected k6 check was silently
        // evaluating against `undefined`.
        var browse = File.ReadAllText(Repo("performance", "load-tests", "scenarios", "browse.js"));
        var authRaces = File.ReadAllText(Repo("performance", "load-tests", "scenarios", "auth-races.js"));
        var program = File.ReadAllText(Repo("HudhudNestApi", "Program.cs"));

        Assert.Contains("PropertyNamingPolicy = JsonNamingPolicy.CamelCase", program, StringComparison.Ordinal);
        Assert.Contains("response.json('items')", browse, StringComparison.Ordinal);
        Assert.Contains("item.distanceMeters", browse, StringComparison.Ordinal);
        Assert.DoesNotContain("response.json('Items')", browse, StringComparison.Ordinal);
        Assert.Contains("response.json('challengeId')", authRaces, StringComparison.Ordinal);

        // BUG-30a: RefreshTokenCookie.Attach (HudhudNestApi/Security/Auth/RefreshTokenCookie.cs)
        // moved the refresh token out of the JSON body and into an HttpOnly `refresh_token`
        // cookie as a security hardening change; PhonePasswordAuthController's Result()
        // helper always nulls the body's RefreshToken field before returning it. auth-races.js
        // must read the cookie, not the (permanently null) body field.
        Assert.Contains("register.cookies.refresh_token", authRaces, StringComparison.Ordinal);
        Assert.DoesNotContain("register.json('refreshToken')", authRaces, StringComparison.Ordinal);

        // BUG-31: ASP.NET Core's ResponseCookies.Append percent-encodes the cookie value
        // it writes ('+' -> '%2B', '/' -> '%2F', '=' -> '%3D'; verified against a real
        // Microsoft.AspNetCore.App ResponseCookiesFeature) and its own Request.Cookies[name]
        // reader percent-decodes it back, so a real browser round trip is symmetric. k6's
        // response.cookies[name][0].value does not decode it (verified against the exact
        // grafana/k6:0.54.0 image this repo's CI uses) -- auth-races.js must decode it
        // itself before sending it back, or every concurrent request hashes to a value with
        // no matching stored token and gets rejected as "not found" instead of racing.
        Assert.Contains("decodeURIComponent(refreshTokenCookie.value)", authRaces, StringComparison.Ordinal);
    }

    [Fact]
    public void Ordinary_pushes_use_the_non_baseline_profile()
    {
        var workflow = File.ReadAllText(Repo(".github", "workflows", "performance-validation.yml"));

        Assert.Contains("github.event_name == 'push') && 'pr'", workflow, StringComparison.Ordinal);
        Assert.Contains("default: release", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void Data_generator_does_not_republish_properties_created_as_published()
    {
        var generator = File.ReadAllText(Repo(
            "tools",
            "HudhudNestApi.PerformanceDataGenerator",
            "Program.cs"));

        Assert.DoesNotContain("property.Publish();", generator, StringComparison.Ordinal);
        Assert.Contains(
            "Property(nameof(property.PublishedAt)).CurrentValue = property.CreatedAt",
            generator,
            StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string Repo(params string[] components)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HudhudNestApi.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(new[] { directory!.FullName }.Concat(components).ToArray());
    }

    private sealed class TestHostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "HudhudNestApi.Performance.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
