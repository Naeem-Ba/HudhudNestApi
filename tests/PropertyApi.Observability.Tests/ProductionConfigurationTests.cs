using PropertyApi.Observability;
using Xunit;

namespace PropertyApi.Observability.Tests;

public sealed class ProductionConfigurationTests
{
    [Fact]
    public void Complete_secure_configuration_is_valid()
    {
        var options = ValidOptions();

        Assert.True(PropertyApiObservabilityValidator.IsValidProductionConfiguration(options));
    }

    [Theory]
    [InlineData(null, "1.0.0", "Production", false)]
    [InlineData("not-a-uri", "1.0.0", "Production", false)]
    [InlineData("http://collector:4317", "1.0.0", "Production", false)]
    [InlineData("https://collector:4317", null, "Production", false)]
    [InlineData("https://collector:4317", "1.0.0", "Staging", false)]
    [InlineData("https://collector:4317", "1.0.0", "Production", true)]
    public void Invalid_production_configuration_is_rejected(
        string? endpoint,
        string? version,
        string environment,
        bool requireMissingAuthentication)
    {
        var options = ValidOptions();
        options.Otlp.Endpoint = endpoint;
        options.ServiceVersion = version;
        options.Environment = environment;
        options.Otlp.RequireAuthentication = requireMissingAuthentication;
        options.Otlp.Headers = requireMissingAuthentication ? null : "authorization=test";

        Assert.False(PropertyApiObservabilityValidator.IsValidProductionConfiguration(options));
    }

    [Fact]
    public void Invalid_sampling_ratio_is_rejected()
    {
        var options = ValidOptions();
        options.Tracing.SamplingRatio = 0;

        Assert.False(PropertyApiObservabilityValidator.IsValidProductionConfiguration(options));
    }

    private static PropertyApiObservabilityOptions ValidOptions() => new()
    {
        Enabled = true,
        ServiceName = "property-api",
        ServiceNamespace = "yaqeen-real-estate",
        ServiceVersion = "1.0.0",
        Environment = "Production",
        Otlp = new PropertyApiOtlpOptions
        {
            Endpoint = "https://collector:4317",
            Protocol = "grpc",
            Headers = "authorization=test",
            RequireAuthentication = true
        }
    };
}
