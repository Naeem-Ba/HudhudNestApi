namespace PropertyApi.Application.Marketing.DTOs;

/// <summary>
/// Aggregate view for the admin dashboard — "معرفة أكثر نماذج الدفع قبولًا" and similar.
/// Averages are computed only over the responses that actually answered that particular
/// question (nulls excluded), never treated as 0 — a null answer is "skipped", not "zero".
/// </summary>
public sealed record SurveyStatsDto(
    int TotalResponses,
    IReadOnlyDictionary<string, int> WillingnessBreakdown,
    IReadOnlyDictionary<string, int> PaymentModelBreakdown,
    decimal? AverageExpectedMonthlyPriceUsd,
    decimal? AverageExpectedPerListingPriceUsd,
    decimal? AverageAcceptableCommissionPercent,
    int TrialWantedCount,
    int UsesSimilarToolCount);
