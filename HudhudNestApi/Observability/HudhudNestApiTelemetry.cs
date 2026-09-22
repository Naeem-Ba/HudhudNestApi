using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;

namespace HudhudNestApi.Observability;

public static class HudhudNestApiTelemetry
{
    public const string ServiceName = "HudhudNestApi";
    public const string ActivitySourceName = "HudhudNestApi.Api";
    public const string MeterName = "HudhudNestApi.Api";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Process CurrentProcess = Process.GetCurrentProcess();
    private static readonly DateTimeOffset ProcessStartedAt = CurrentProcess.StartTime.ToUniversalTime();

    private static readonly ObservableGauge<long> ProcessWorkingSet =
        Meter.CreateObservableGauge(
            "process.memory.working_set",
            () => CurrentProcess.WorkingSet64,
            unit: "By",
            description: "Physical memory held by the HudhudNestApi process.");

    private static readonly ObservableGauge<double> ProcessUptime =
        Meter.CreateObservableGauge(
            "process.uptime",
            () => (DateTimeOffset.UtcNow - ProcessStartedAt).TotalSeconds,
            unit: "s",
            description: "HudhudNestApi process uptime.");

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

    // Staging-only: toggled exclusively by ObservabilitySyntheticController's alert-test-state
    // endpoint, which is itself gated by Staging:TestSupport:Enabled and a shared secret. Exists
    // solely so scripts/verify-observability.sh can exercise a full Prometheus alert lifecycle
    // (firing then resolving) against a real, permanently-configured alert rule instead of
    // rewriting rule files on disk at runtime -- see observability/render/prometheus/rules.
    private static long _syntheticAlertTestState;

    private static readonly ObservableGauge<long> SyntheticAlertTestState =
        Meter.CreateObservableGauge(
            "propertyapi.synthetic.alert_test_state",
            () => Interlocked.Read(ref _syntheticAlertTestState),
            description: "Staging-only synthetic gauge toggled by the observability smoke test " +
                "to exercise a full Prometheus alert firing/resolution cycle. Always 0 outside " +
                "controlled test invocations.");

    public static void SetSyntheticAlertTestState(bool firing) =>
        Interlocked.Exchange(ref _syntheticAlertTestState, firing ? 1 : 0);

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
