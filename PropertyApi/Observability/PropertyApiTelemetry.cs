using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PropertyApi.Observability;

public static class PropertyApiTelemetry
{
    public const string ServiceName = "PropertyApi";
    public const string ActivitySourceName = "PropertyApi.Api";
    public const string MeterName = "PropertyApi.Api";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Process CurrentProcess = Process.GetCurrentProcess();
    private static readonly DateTimeOffset ProcessStartedAt = CurrentProcess.StartTime.ToUniversalTime();

    private static readonly ObservableGauge<long> ProcessWorkingSet =
        Meter.CreateObservableGauge(
            "process.memory.working_set",
            () => CurrentProcess.WorkingSet64,
            unit: "By",
            description: "Physical memory held by the PropertyApi process.");

    private static readonly ObservableGauge<double> ProcessUptime =
        Meter.CreateObservableGauge(
            "process.uptime",
            () => (DateTimeOffset.UtcNow - ProcessStartedAt).TotalSeconds,
            unit: "s",
            description: "PropertyApi process uptime.");

    private static readonly Counter<long> HttpRequestCounter =
        Meter.CreateCounter<long>(
            "propertyapi.http.server.requests",
            unit: "requests",
            description: "Completed HTTP requests grouped by low-cardinality API operation.");

    private static readonly Histogram<double> HttpRequestDuration =
        Meter.CreateHistogram<double>(
            "propertyapi.http.server.duration",
            unit: "ms",
            description: "HTTP request duration in milliseconds.");

    private static readonly Counter<long> AuthRequestCounter =
        Meter.CreateCounter<long>(
            "propertyapi.auth.requests",
            unit: "requests",
            description: "Authentication requests grouped by operation and outcome.");

    private static readonly Histogram<double> AuthRequestDuration =
        Meter.CreateHistogram<double>(
            "propertyapi.auth.duration",
            unit: "ms",
            description: "Authentication request duration in milliseconds.");

    private static readonly IReadOnlyDictionary<string, Counter<long>> AuthAttemptCounters =
        new Dictionary<string, Counter<long>>(StringComparer.OrdinalIgnoreCase)
        {
            ["register"] = Meter.CreateCounter<long>("auth.registration.attempts"),
            ["login"] = Meter.CreateCounter<long>("auth.login.attempts"),
            ["refresh"] = Meter.CreateCounter<long>("auth.refresh.attempts"),
            ["logout"] = Meter.CreateCounter<long>("auth.logout.attempts"),
            ["send_otp"] = Meter.CreateCounter<long>("auth.otp.requests")
        };

    private static readonly IReadOnlyDictionary<string, Counter<long>> AuthFailureCounters =
        new Dictionary<string, Counter<long>>(StringComparer.OrdinalIgnoreCase)
        {
            ["register"] = Meter.CreateCounter<long>("auth.registration.failures"),
            ["login"] = Meter.CreateCounter<long>("auth.login.failures"),
            ["refresh"] = Meter.CreateCounter<long>("auth.refresh.failures"),
            ["logout"] = Meter.CreateCounter<long>("auth.logout.failures"),
            ["send_otp"] = Meter.CreateCounter<long>("auth.otp.failures"),
            ["verify_otp"] = Meter.CreateCounter<long>("auth.otp.verification.failures")
        };

    private static readonly Counter<long> PropertyRequestCounter =
        Meter.CreateCounter<long>(
            "propertyapi.properties.requests",
            unit: "requests",
            description: "Property API requests grouped by operation and outcome.");

    private static readonly Histogram<double> PropertyRequestDuration =
        Meter.CreateHistogram<double>(
            "propertyapi.properties.duration",
            unit: "ms",
            description: "Property API request duration in milliseconds.");

    public static void RecordHttpRequest(
        RequestTelemetryRoute route,
        string method,
        int statusCode,
        string outcome,
        double elapsedMilliseconds)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("route_group", route.RouteGroup),
            new("operation", route.Operation),
            new("method", method),
            new("status_code", statusCode),
            new("outcome", outcome)
        };

        HttpRequestCounter.Add(1, tags);
        HttpRequestDuration.Record(elapsedMilliseconds, tags);

        if (route.IsAuth)
        {
            RecordAuthRequest(route.Operation, outcome, elapsedMilliseconds);
        }

        if (route.IsProperties)
        {
            RecordPropertyRequest(route.Operation, outcome, elapsedMilliseconds);
        }
    }

    private static void RecordAuthRequest(
        string operation,
        string outcome,
        double elapsedMilliseconds)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("operation", operation),
            new("outcome", outcome)
        };

        AuthRequestCounter.Add(1, tags);
        AuthRequestDuration.Record(elapsedMilliseconds, tags);

        if (AuthAttemptCounters.TryGetValue(operation, out var attempts))
        {
            attempts.Add(1, new KeyValuePair<string, object?>("operation", operation));
        }

        if (!outcome.Equals("success", StringComparison.OrdinalIgnoreCase) &&
            AuthFailureCounters.TryGetValue(operation, out var failures))
        {
            failures.Add(1,
                new KeyValuePair<string, object?>("operation", operation),
                new KeyValuePair<string, object?>("failure_reason_category", "unknown"));
        }
    }

    private static void RecordPropertyRequest(
        string operation,
        string outcome,
        double elapsedMilliseconds)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("operation", operation),
            new("outcome", outcome)
        };

        PropertyRequestCounter.Add(1, tags);
        PropertyRequestDuration.Record(elapsedMilliseconds, tags);
    }
}
