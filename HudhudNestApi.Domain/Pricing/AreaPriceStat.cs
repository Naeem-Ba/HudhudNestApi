namespace HudhudNestApi.Domain.Pricing;

public enum PriceKind
{
    Sale = 1,

    /// <summary>Monthly long-term rent (cold rent when given). Short-stay nightly prices are never mixed in.</summary>
    Rent = 2,
}

public enum EstimateConfidence
{
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>
/// Growth engine P6: asking-price statistics per square metre for one area, one price kind and optionally one
/// property type, recomputed by a background job. A row exists only when enough listings remained after
/// removing duplicates and outliers; "no row" means "not enough data", never zero.
/// Prices are asking prices of live listings (not sale prices), converted to USD at the site's rates.
/// </summary>
public sealed class AreaPriceStat
{
    public long Id { get; private set; }
    public int GovernorateId { get; private set; }
    /// <summary>Null for the whole governorate.</summary>
    public int? DistrictId { get; private set; }
    /// <summary>Null for all property types together.</summary>
    public int? PropertyTypeId { get; private set; }
    public PriceKind Kind { get; private set; }
    public int SampleCount { get; private set; }
    public int ExcludedCount { get; private set; }
    public decimal P25PerSqmUsd { get; private set; }
    public decimal MedianPerSqmUsd { get; private set; }
    public decimal P75PerSqmUsd { get; private set; }
    public EstimateConfidence Confidence { get; private set; }
    public DateTime OldestListingAt { get; private set; }
    public DateTime NewestListingAt { get; private set; }
    public DateTime ComputedAt { get; private set; }

    private AreaPriceStat() { }

    public static AreaPriceStat Create(
        int governorateId, int? districtId, int? propertyTypeId, PriceKind kind,
        int sampleCount, int excludedCount, decimal p25, decimal median, decimal p75,
        EstimateConfidence confidence, DateTime oldest, DateTime newest, DateTime computedAt) =>
        new()
        {
            GovernorateId = governorateId,
            DistrictId = districtId,
            PropertyTypeId = propertyTypeId,
            Kind = kind,
            SampleCount = sampleCount,
            ExcludedCount = excludedCount,
            P25PerSqmUsd = p25,
            MedianPerSqmUsd = median,
            P75PerSqmUsd = p75,
            Confidence = confidence,
            OldestListingAt = oldest,
            NewestListingAt = newest,
            ComputedAt = computedAt,
        };
}
