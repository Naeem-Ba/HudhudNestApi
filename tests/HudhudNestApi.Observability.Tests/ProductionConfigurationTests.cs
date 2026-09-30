using System.Text.Json;
using HudhudNestApi.Observability;
using Xunit;

namespace HudhudNestApi.Observability.Tests;

public sealed class ProductionConfigurationTests
{
    [Fact]
    public void Complete_secure_configuration_is_valid()
    {
        var options = ValidOptions();

        Assert.True(HudhudNestApiObservabilityValidator.IsValidProductionConfiguration(options));
    }

    [Theory]
    [InlineData("not-a-uri", false)]
    [InlineData("http://collector:4317", false)]
    [InlineData("https://collector:4317", true)]
    public void Invalid_production_otlp_configuration_is_rejected(
        string endpoint,
        bool requireMissingAuthentication)
    {
        var options = ValidOptions();
        options.Otlp.Endpoint = endpoint;
        options.Otlp.RequireAuthentication = requireMissingAuthentication;
        options.Otlp.Headers = requireMissingAuthentication ? null : "authorization=test";

        Assert.False(HudhudNestApiObservabilityValidator.IsValidProductionConfiguration(options));
    }

    [Fact]
    public void Invalid_sampling_ratio_is_rejected()
    {
        var options = ValidOptions();
        options.Tracing.SamplingRatio = 0;

        Assert.False(HudhudNestApiObservabilityValidator.IsValidProductionConfiguration(options));
    }

    [Fact]
    public void Production_configuration_without_external_otlp_export_is_valid()
    {
        var options = ValidOptions();
        options.ServiceVersion = null;
        options.Environment = null;
        options.Otlp.Endpoint = null;
        options.Otlp.Headers = null;
        options.Otlp.RequireAuthentication = false;

        Assert.True(HudhudNestApiObservabilityValidator.IsValidProductionConfiguration(options));
    }

    [Fact]
    public void Testing_environment_disables_observability_hosted_services()
    {
        var repositoryRoot = FindRepositoryRoot();
        var settingsPath = Path.Combine(
            repositoryRoot,
            "HudhudNestApi",
            "appsettings.Testing.json");

        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));

        Assert.False(document.RootElement
            .GetProperty("Observability")
            .GetProperty("Enabled")
            .GetBoolean());
    }

    private static HudhudNestApiObservabilityOptions ValidOptions() => new()
    {
        Enabled = true,
        ServiceName = "property-api",
        ServiceNamespace = "yaqeen-real-estate",
        ServiceVersion = "1.0.0",
        Environment = "Production",
        Otlp = new HudhudNestApiOtlpOptions
        {
            Endpoint = "https://collector:4317",
            Protocol = "grpc",
            Headers = "authorization=test",
            RequireAuthentication = true
        }
    };

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HudhudNestApi.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate repository root containing HudhudNestApi.sln.");
    }
}
