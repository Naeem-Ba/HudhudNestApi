using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HudhudNestApi.Application.Common.Observability;

public static class ApplicationTelemetry
{
    public const string ActivitySourceName = "HudhudNestApi.Application";
    public const string MeterName = "HudhudNestApi.Application";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
    public static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> RequestCounter =
        Meter.CreateCounter<long>(
            "hudhudnest.application.requests",
            unit: "requests",
            description: "Application requests grouped by request kind and outcome.");

    private static readonly Histogram<double> RequestDuration =
        Meter.CreateHistogram<double>(
            "hudhudnest.application.request.duration",
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

    private static readonly Counter<long> OtpSendCounter =
        Meter.CreateCounter<long>(
            "auth.otp.send",
            unit: "sends",
            description:
                "Phone OTP sends by channel and outcome (sent, unreachable, provider_unavailable, " +
                "channel_unavailable, rate_limited). Never labelled with a phone number.");

    private static readonly Counter<long> OtpVerificationCounter =
        Meter.CreateCounter<long>(
            "auth.otp.verification",
            unit: "verifications",
            description: "Phone OTP verification attempts by channel and outcome (succeeded, failed).");

    /// <summary>Channel and outcome are small fixed sets, so the label cardinality stays bounded.</summary>
    public static void RecordOtpSend(string channel, string outcome)
        => OtpSendCounter.Add(
            1,
            new KeyValuePair<string, object?>("channel", channel),
            new KeyValuePair<string, object?>("outcome", outcome));

    public static void RecordOtpVerification(string channel, string outcome)
        => OtpVerificationCounter.Add(
            1,
            new KeyValuePair<string, object?>("channel", channel),
            new KeyValuePair<string, object?>("outcome", outcome));

    private static readonly Counter<long> BreachScreeningCounter =
        Meter.CreateCounter<long>(
            "auth.password_breach_screening",
            unit: "checks",
            description:
                "Password breach-screening attempts by outcome. Screening fails open, so " +
                "'unavailable' and 'circuit_open' mean passwords were accepted without " +
                "being checked -- alert on those rather than reading silence as safety.");

    /// <summary>
    /// Outcome is one of: clean, breached, unavailable, circuit_open.
    /// </summary>
    public static void RecordPasswordBreachScreening(string outcome)
        => BreachScreeningCounter.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcome));
}
