namespace HudhudNestApi.Observability;

public sealed class HudhudNestApiObservabilityOptions
{
    public const string SectionName = "Observability";

    public bool Enabled { get; set; } = true;

    public string ServiceName { get; set; } = "hudhudnest-api";

    public string ServiceNamespace { get; set; } = "yaqeen-real-estate";

    public string? ServiceVersion { get; set; }

    public string? Environment { get; set; }

    public string? GitCommitSha { get; set; }

    public string CorrelationHeaderName { get; set; } = "X-Correlation-ID";

    public bool RequestLoggingEnabled { get; set; } = true;

    public bool JsonConsoleEnabled { get; set; }

    public HudhudNestApiOtlpOptions Otlp { get; set; } = new();

    public HudhudNestApiTracingOptions Tracing { get; set; } = new();

    public HudhudNestApiMetricsOptions Metrics { get; set; } = new();
}

public sealed class HudhudNestApiOtlpOptions
{
    public string? Endpoint { get; set; }

    public string Protocol { get; set; } = "grpc";

    public string? Headers { get; set; }

    public bool RequireAuthentication { get; set; }

    public int ExportTimeoutMilliseconds { get; set; } = 10_000;
}

public sealed class HudhudNestApiTracingOptions
{
    public bool Enabled { get; set; } = true;

    public double SamplingRatio { get; set; } = 1.0;

    public bool RecordExceptions { get; set; } = true;
}

public sealed class HudhudNestApiMetricsOptions
{
    public bool Enabled { get; set; } = true;

    public int ExportIntervalMilliseconds { get; set; } = 30_000;
}
