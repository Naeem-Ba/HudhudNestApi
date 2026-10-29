using System.Diagnostics.Metrics;
using HudhudNestApi.Application.Common.Observability;

namespace HudhudNestApi.Application.Listings.Translations;

/// <summary>Counters for the translation feature. Labels are small fixed sets - never a listing id, text or key.</summary>
public static class ListingTranslationTelemetry
{
    private static readonly Counter<long> Outcomes = ApplicationTelemetry.Meter.CreateCounter<long>(
        "hudhudnest.listing_translation.outcomes",
        description:
            "Per-field translation outcomes: reused (stored row served), created (provider call stored), same_language, " +
            "unknown_language, pending_elsewhere (another request owns it), failed, timeout, quota, disabled, source_changed " +
            "(text edited or listing deleted while translating - result discarded), cooldown.");

    private static readonly Counter<long> ProviderCharacters = ApplicationTelemetry.Meter.CreateCounter<long>(
        "hudhudnest.listing_translation.provider_characters",
        unit: "characters",
        description: "Source characters sent to the translation provider (the quota it bills or limits on).");

    private static readonly Histogram<double> ProviderDuration = ApplicationTelemetry.Meter.CreateHistogram<double>(
        "hudhudnest.listing_translation.provider_duration",
        unit: "ms",
        description: "Wall-clock time of one provider call (batch of fields).");

    public static void Outcome(string outcome, string target)
        => Outcomes.Add(1, new KeyValuePair<string, object?>("outcome", outcome), new KeyValuePair<string, object?>("target", target));

    public static void Characters(string provider, long characters)
        => ProviderCharacters.Add(characters, new KeyValuePair<string, object?>("provider", provider));

    public static void Duration(string provider, string outcome, double milliseconds)
        => ProviderDuration.Record(milliseconds, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("outcome", outcome));
}
