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

    private static readonly IReadOnlyDictionary<string, Counter<long>> AuthenticationCounters =
        new Dictionary<string, Counter<long>>(StringComparer.Ordinal)
        {
            ["account_resolution"] = Meter.CreateCounter<long>("auth.account_resolution"),
            ["account_creation"] = Meter.CreateCounter<long>("auth.account_creation"),
            ["account_linking"] = Meter.CreateCounter<long>("auth.account_linking"),
            ["phone_verification"] = Meter.CreateCounter<long>("auth.phone_verification"),
            ["session_issuance"] = Meter.CreateCounter<long>("auth.session_issuance"),
            ["compensation"] = Meter.CreateCounter<long>("auth.compensation"),
            ["concurrency_conflict"] = Meter.CreateCounter<long>("auth.concurrency_conflict")
        };

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

    public static void RecordAuthenticationStage(
        string stage,
        string outcome,
        string method)
    {
        if (!AuthenticationCounters.TryGetValue(stage, out var counter))
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        counter.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("authentication_method", method));
    }
}
