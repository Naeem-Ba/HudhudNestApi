namespace HudhudNestApi.Observability;

public static class HudhudNestApiObservabilityValidator
{
    public static bool IsValidProductionConfiguration(HudhudNestApiObservabilityOptions options)
    {
        var hasOtlpEndpoint = !string.IsNullOrWhiteSpace(options.Otlp.Endpoint);
        var hasValidOtlpConfiguration = !hasOtlpEndpoint ||
            (Uri.TryCreate(options.Otlp.Endpoint, UriKind.Absolute, out var endpoint) &&
             endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
             (!options.Otlp.RequireAuthentication ||
              !string.IsNullOrWhiteSpace(options.Otlp.Headers)));

        return options.Enabled &&
               !string.IsNullOrWhiteSpace(options.ServiceName) &&
               !string.IsNullOrWhiteSpace(options.ServiceNamespace) &&
               options.Tracing.Enabled &&
               options.Metrics.Enabled &&
               options.Tracing.SamplingRatio is > 0 and <= 1 &&
               options.Otlp.ExportTimeoutMilliseconds is >= 1_000 and <= 60_000 &&
               hasValidOtlpConfiguration;
    }
}
