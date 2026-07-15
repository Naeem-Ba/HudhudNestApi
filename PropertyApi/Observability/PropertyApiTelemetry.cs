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
