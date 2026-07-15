using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PropertyApi.Application.Common.Observability;

public static class ApplicationTelemetry
{
    public const string ActivitySourceName = "PropertyApi.Application";
    public const string MeterName = "PropertyApi.Application";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> RequestCounter =
        Meter.CreateCounter<long>(
            "propertyapi.application.requests",
            unit: "requests",
            description: "Application requests grouped by request kind and outcome.");

    private static readonly Histogram<double> RequestDuration =
        Meter.CreateHistogram<double>(
            "propertyapi.application.request.duration",
            unit: "ms",
            description: "Application request duration in milliseconds.");

    public static void RecordRequest(
        string requestKind,
        string outcome,
        double elapsedMilliseconds)
    {
        var tags = new KeyValuePair<string, object?>[]
        {
            new("request_kind", requestKind),
            new("outcome", outcome)
        };

        RequestCounter.Add(1, tags);
        RequestDuration.Record(elapsedMilliseconds, tags);
    }
}
