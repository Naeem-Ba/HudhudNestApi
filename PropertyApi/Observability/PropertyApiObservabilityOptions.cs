namespace PropertyApi.Observability;

public sealed class PropertyApiObservabilityOptions
{
    public const string SectionName = "Observability";

    public string ServiceName { get; set; } = PropertyApiTelemetry.ServiceName;

    public string? ServiceVersion { get; set; }

    public string CorrelationHeaderName { get; set; } = "X-Correlation-ID";

    public bool RequestLoggingEnabled { get; set; } = true;

    public bool JsonConsoleEnabled { get; set; }

    public PropertyApiOtlpOptions Otlp { get; set; } = new();
}

public sealed class PropertyApiOtlpOptions
{
    public string? Endpoint { get; set; }

    public string Protocol { get; set; } = "grpc";

    public string? Headers { get; set; }
}
