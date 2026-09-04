using System.Diagnostics;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PropertyApi.Application.Common.Observability;

namespace PropertyApi.Observability;

public static class PropertyApiObservabilityExtensions
{
    private static readonly string[] AllowedOtlpProtocols = ["grpc", "http/protobuf"];

    public static WebApplicationBuilder AddPropertyApiObservability(
        this WebApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<PropertyApiObservabilityOptions>()
            .Bind(builder.Configuration.GetSection(PropertyApiObservabilityOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(options => !string.IsNullOrWhiteSpace(options.ServiceName),
                "Observability:ServiceName is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.ServiceNamespace),
                "Observability:ServiceNamespace is required.")
            .Validate(options => string.IsNullOrWhiteSpace(options.Otlp.Endpoint) ||
                Uri.TryCreate(options.Otlp.Endpoint, UriKind.Absolute, out _),
                "Observability:Otlp:Endpoint must be an absolute URI when configured.")
            .Validate(options => AllowedOtlpProtocols.Contains(
                    options.Otlp.Protocol, StringComparer.OrdinalIgnoreCase),
                "Observability:Otlp:Protocol must be grpc or http/protobuf.")
            .Validate(options => options.Tracing.SamplingRatio is >= 0 and <= 1,
                "Observability:Tracing:SamplingRatio must be between 0 and 1.")
            .Validate(options => options.Otlp.ExportTimeoutMilliseconds is >= 1_000 and <= 60_000,
                "Observability:Otlp:ExportTimeoutMilliseconds must be between 1000 and 60000.")
            .Validate(options => options.Metrics.ExportIntervalMilliseconds is >= 1_000 and <= 300_000,
                "Observability:Metrics:ExportIntervalMilliseconds must be between 1000 and 300000.")
            .Validate(options => !builder.Environment.IsProduction() ||
                PropertyApiObservabilityValidator.IsValidProductionConfiguration(options),
                "Production observability requires Enabled=true with tracing and metrics enabled. " +
                "When OTLP is configured, its endpoint must use HTTPS and authentication headers are " +
                "required when RequireAuthentication=true.")
            .ValidateOnStart();

        ConfigureLogging(builder);

        var options = builder.Configuration
            .GetSection(PropertyApiObservabilityOptions.SectionName)
            .Get<PropertyApiObservabilityOptions>() ?? new();

        if (!options.Enabled)
        {
            return builder;
        }

        var resource = CreateResource(builder, options);
        var openTelemetry = builder.Services.AddOpenTelemetry().ConfigureResource(resourceBuilder =>
            resourceBuilder.AddAttributes(resource));

        if (options.Tracing.Enabled)
        {
            openTelemetry.WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new ParentBasedSampler(
                        new TraceIdRatioBasedSampler(options.Tracing.SamplingRatio)))
                    .AddSource(PropertyApiTelemetry.ActivitySourceName)
                    .AddSource(ApplicationTelemetry.ActivitySourceName)
                    .AddAspNetCoreInstrumentation(instrumentation =>
                    {
                        instrumentation.RecordException = options.Tracing.RecordExceptions;
                        instrumentation.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health/live");
                        instrumentation.EnrichWithHttpRequest = (activity, request) =>
                        {
                            if (request.HttpContext.Items.TryGetValue("CorrelationId", out var correlationId))
                            {
                                activity.SetTag("propertyapi.correlation_id", correlationId?.ToString());
                            }
                        };
                    })
                    .AddHttpClientInstrumentation(instrumentation =>
                    {
                        instrumentation.RecordException = options.Tracing.RecordExceptions;
                        instrumentation.FilterHttpRequestMessage = request =>
                            request.RequestUri is not null &&
                            !request.RequestUri.IsLoopback;
                    })
                    .AddNpgsql()
                    .AddRedisInstrumentation(redis =>
                    {
                        redis.SetVerboseDatabaseStatements = false;
                    })
                    .AddProcessor(new TelemetryRedactionProcessor());

                AddTraceOtlpExporter(tracing, options);
            });
        }

        if (options.Metrics.Enabled)
        {
            openTelemetry.WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(PropertyApiTelemetry.MeterName)
                    .AddMeter(ApplicationTelemetry.MeterName)
                    .AddMeter("Npgsql")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddView("http.server.request.duration", new ExplicitBucketHistogramConfiguration
                    {
                        Boundaries = [0.1, 0.25, 0.5, 1, 2, 5]
                    })
                    .AddView("http.client.request.duration", new ExplicitBucketHistogramConfiguration
                    {
                        Boundaries = [0.1, 0.25, 0.5, 1, 2, 5]
                    });

                AddMetricOtlpExporter(metrics, options);
            });
        }

        return builder;
    }

    public static IApplicationBuilder UsePropertyApiObservability(this IApplicationBuilder app)
    {
        _ = app.ApplicationServices
            .GetRequiredService<IOptions<PropertyApiObservabilityOptions>>()
            .Value;

        return app.UseMiddleware<CorrelationIdMiddleware>();
    }

    private static IReadOnlyDictionary<string, object> CreateResource(
        WebApplicationBuilder builder,
        PropertyApiObservabilityOptions options)
    {
        var environment = string.IsNullOrWhiteSpace(options.Environment)
            ? builder.Environment.EnvironmentName
            : options.Environment;
        var commitSha = !string.IsNullOrWhiteSpace(options.GitCommitSha)
            ? options.GitCommitSha
            : Environment.GetEnvironmentVariable("RENDER_GIT_COMMIT")
              ?? Environment.GetEnvironmentVariable("GITHUB_SHA");
        var serviceVersion = string.IsNullOrWhiteSpace(options.ServiceVersion)
            ? commitSha ?? "development"
            : options.ServiceVersion;
        var instanceId = Environment.GetEnvironmentVariable("RENDER_INSTANCE_ID")
            ?? Environment.GetEnvironmentVariable("HOSTNAME")
            ?? Environment.MachineName;

        var attributes = new Dictionary<string, object>
        {
            ["service.name"] = options.ServiceName,
            ["service.namespace"] = options.ServiceNamespace,
            ["service.version"] = serviceVersion,
            ["service.instance.id"] = instanceId,
            ["deployment.environment.name"] = environment
        };

        if (!string.IsNullOrWhiteSpace(commitSha))
        {
            attributes["git.commit.sha"] = commitSha;
        }

        return attributes;
    }

    private static void AddTraceOtlpExporter(
        TracerProviderBuilder tracing,
        PropertyApiObservabilityOptions options)
    {
        if (!TryGetEndpoint(options, out var endpoint))
        {
            return;
        }

        tracing.AddOtlpExporter(exporter =>
            ConfigureExporter(exporter, options, endpoint, "v1/traces"));
    }

    private static void AddMetricOtlpExporter(
        MeterProviderBuilder metrics,
        PropertyApiObservabilityOptions options)
    {
        if (!TryGetEndpoint(options, out var endpoint))
        {
            return;
        }

        metrics.AddOtlpExporter((exporter, reader) =>
        {
            ConfigureExporter(exporter, options, endpoint, "v1/metrics");
            reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                options.Metrics.ExportIntervalMilliseconds;
            reader.PeriodicExportingMetricReaderOptions.ExportTimeoutMilliseconds =
                options.Otlp.ExportTimeoutMilliseconds;
        });
    }

    private static void ConfigureExporter(
        OtlpExporterOptions exporter,
        PropertyApiObservabilityOptions options,
        Uri endpoint,
        string signalPath)
    {
        var isHttpProtobuf = !options.Otlp.Protocol.Equals("grpc", StringComparison.OrdinalIgnoreCase);

        // The .NET SDK only appends a signal-specific path (v1/traces, v1/metrics) to the
        // *generic* OTEL_EXPORTER_OTLP_ENDPOINT env var it reads itself; setting
        // OtlpExporterOptions.Endpoint directly in code, as we do here, is treated as an
        // already-complete, signal-specific endpoint and used verbatim -- confirmed by curling
        // a real deployed collector: the base URL alone got 404, only .../v1/traces got 200.
        // So for HTTP/protobuf we append the standard OTLP path ourselves whenever the
        // configured endpoint is just a bare host (no path of its own); gRPC needs no path.
        exporter.Endpoint = isHttpProtobuf && IsBareHost(endpoint)
            ? new Uri(endpoint, signalPath)
            : endpoint;
        exporter.Protocol = isHttpProtobuf
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;
        exporter.Headers = string.IsNullOrWhiteSpace(options.Otlp.Headers)
            ? null
            : options.Otlp.Headers;
        exporter.TimeoutMilliseconds = options.Otlp.ExportTimeoutMilliseconds;
    }

    private static bool IsBareHost(Uri endpoint) =>
        endpoint.AbsolutePath is "" or "/";

    private static bool TryGetEndpoint(
        PropertyApiObservabilityOptions options,
        out Uri endpoint)
    {
        return Uri.TryCreate(options.Otlp.Endpoint, UriKind.Absolute, out endpoint!);
    }

    private static void ConfigureLogging(WebApplicationBuilder builder)
    {
        builder.Logging.Configure(options =>
        {
            options.ActivityTrackingOptions =
                ActivityTrackingOptions.TraceId |
                ActivityTrackingOptions.SpanId |
                ActivityTrackingOptions.ParentId |
                ActivityTrackingOptions.Tags;
        });

        var options = builder.Configuration
            .GetSection(PropertyApiObservabilityOptions.SectionName)
            .Get<PropertyApiObservabilityOptions>() ?? new();

        if (options.JsonConsoleEnabled)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddJsonConsole(console =>
            {
                console.IncludeScopes = true;
                console.TimestampFormat = "O";
                console.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
            });
        }
        else
        {
            builder.Services.Configure<SimpleConsoleFormatterOptions>(console =>
            {
                console.IncludeScopes = true;
                console.TimestampFormat = "O";
            });
        }
    }
}
