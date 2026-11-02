using HudhudNestApi.Domain.Pricing;

namespace HudhudNestApi.Application.Pricing;

/// <summary>Settings for area statistics and the estimator (section "AreaPrices"). See METHODOLOGY in docs.</summary>
public sealed class AreaPriceOptions
{
    public const string SectionName = "AreaPrices";

    /// <summary>Fewest listings (after cleaning) before anything is shown for a group.</summary>
    public int MinSample { get; set; } = 8;

    /// <summary>Fewest different owners in a group: one agency's many listings must not set an area's price alone.</summary>
    public int MinDistinctOwners { get; set; } = 3;

    /// <summary>Tukey fence factor: per-m² prices outside [P25 − k·IQR, P75 + k·IQR] are dropped.</summary>
    public decimal OutlierIqrFactor { get; set; } = 1.5m;

    /// <summary>Listings (and estimate requests) outside this size range are not used.</summary>
    public decimal MinAreaSqm { get; set; } = 15m;
    public decimal MaxAreaSqm { get; set; } = 5000m;

    /// <summary>Sample sizes from which confidence is Medium and High.</summary>
    public int MediumConfidenceFrom { get; set; } = 15;
    public int HighConfidenceFrom { get; set; } = 40;

    /// <summary>When (P75 − P25) / median is above this, confidence drops one level (prices too spread).</summary>
    public decimal WideSpreadRatio { get; set; } = 0.6m;

    public int RecomputeHours { get; set; } = 6;
}

/// <summary>One live listing as the statistics see it: price already in USD, area in m².</summary>
public sealed record PriceSample(Guid OwnerId, int? DistrictId, decimal AreaSqm, decimal PriceUsd, DateTime PublishedAt);

public sealed record PriceSummary(
    int SampleCount, int ExcludedCount, decimal P25, decimal Median, decimal P75,
    EstimateConfidence Confidence, DateTime Oldest, DateTime Newest);

/// <summary>
/// Pure statistics, no I/O. Steps: drop sizes out of range → collapse duplicates (same owner, district, area and
/// price: the same flat posted twice) → per-m² price → drop outliers by Tukey fences → require the minimum sample
/// and at least a few different owners →
/// quartiles by linear interpolation → confidence from sample size and spread.
/// </summary>
public static class AreaPriceStatistics
{
    public static PriceSummary? Summarize(IReadOnlyCollection<PriceSample> samples, AreaPriceOptions o)
    {
        var inRange = samples
            .Where(s => s.AreaSqm >= o.MinAreaSqm && s.AreaSqm <= o.MaxAreaSqm && s.PriceUsd > 0)
            .ToList();

        var unique = inRange
            .GroupBy(s => (s.OwnerId, s.DistrictId, Area: decimal.Round(s.AreaSqm, 0), Price: decimal.Round(s.PriceUsd, 0)))
            .Select(g => g.OrderByDescending(s => s.PublishedAt).First())
            .ToList();

        var excluded = samples.Count - unique.Count;
        if (unique.Count < o.MinSample)
            return null;

        var perSqm = unique.Select(s => (Value: s.PriceUsd / s.AreaSqm, s.PublishedAt, Sample: s)).OrderBy(x => x.Value).ToList();
        var q1 = Quantile(perSqm.Select(x => x.Value).ToList(), 0.25m);
        var q3 = Quantile(perSqm.Select(x => x.Value).ToList(), 0.75m);
        var fence = (q3 - q1) * o.OutlierIqrFactor;
        var kept = perSqm.Where(x => x.Value >= q1 - fence && x.Value <= q3 + fence).ToList();
        excluded += perSqm.Count - kept.Count;

        if (kept.Count < o.MinSample)
            return null;
        if (kept.Select(k => k.Sample.OwnerId).Distinct().Count() < o.MinDistinctOwners)
            return null;

        var values = kept.Select(x => x.Value).ToList();
        var p25 = Quantile(values, 0.25m);
        var median = Quantile(values, 0.5m);
        var p75 = Quantile(values, 0.75m);

        var confidence = kept.Count >= o.HighConfidenceFrom ? EstimateConfidence.High
            : kept.Count >= o.MediumConfidenceFrom ? EstimateConfidence.Medium
            : EstimateConfidence.Low;
        if (median > 0 && (p75 - p25) / median > o.WideSpreadRatio && confidence > EstimateConfidence.Low)
            confidence--;

        return new PriceSummary(
            kept.Count, excluded, Round(p25), Round(median), Round(p75), confidence,
            kept.Min(x => x.PublishedAt), kept.Max(x => x.PublishedAt));
    }

    /// <summary>Linear interpolation between closest ranks (the common "type 7" definition). Input sorted.</summary>
    public static decimal Quantile(IReadOnlyList<decimal> sorted, decimal q)
    {
        if (sorted.Count == 0)
            throw new ArgumentException("No values.", nameof(sorted));
        var position = (sorted.Count - 1) * q;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
    }

    private static decimal Round(decimal v) => decimal.Round(v, 2, MidpointRounding.AwayFromZero);
}
