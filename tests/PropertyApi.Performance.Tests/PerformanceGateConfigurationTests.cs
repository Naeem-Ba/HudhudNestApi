using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PropertyApi.Infrastructure.Performance;
using PropertyApi.Performance;
using Xunit;

namespace PropertyApi.Performance.Tests;

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
    public void Budgets_define_every_release_blocking_metric_and_remain_unapproved_until_measured()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Repo("performance", "performance-budgets.json")));
        var root = document.RootElement;
        Assert.False(root.GetProperty("approved").GetBoolean());
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
    public void Data_generator_does_not_republish_properties_created_as_published()
    {
        var generator = File.ReadAllText(Repo(
            "tools",
            "PropertyApi.PerformanceDataGenerator",
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
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PropertyApi.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(new[] { directory!.FullName }.Concat(components).ToArray());
    }

    private sealed class TestHostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "PropertyApi.Performance.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
