namespace PropertyApi.Observability;

public static class PropertyApiObservabilityValidator
{
    public static bool IsValidProductionConfiguration(PropertyApiObservabilityOptions options)
    {
        return options.Enabled &&
               options.Environment?.Equals("Production", StringComparison.OrdinalIgnoreCase) == true &&
               !string.IsNullOrWhiteSpace(options.ServiceName) &&
               !string.IsNullOrWhiteSpace(options.ServiceNamespace) &&
               !string.IsNullOrWhiteSpace(options.ServiceVersion) &&
               options.Tracing.Enabled &&
               options.Metrics.Enabled &&
               options.Tracing.SamplingRatio is > 0 and <= 1 &&
               options.Otlp.ExportTimeoutMilliseconds is >= 1_000 and <= 60_000 &&
               Uri.TryCreate(options.Otlp.Endpoint, UriKind.Absolute, out var endpoint) &&
               endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
               (!options.Otlp.RequireAuthentication || !string.IsNullOrWhiteSpace(options.Otlp.Headers));
    }
}
