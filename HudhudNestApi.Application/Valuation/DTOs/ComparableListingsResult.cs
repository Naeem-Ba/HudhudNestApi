using HudhudNestApi.Domain.Valuation.Enums;

namespace HudhudNestApi.Application.Valuation.DTOs;

/// <summary>
/// Result of the Valuation Fast Path (Phase 3): a Preliminary Valuation computed from
/// currently-listed comparable Properties — never a Final Valuation. Mirrors
/// DuplicateCheckResultDto's shape (a plain settable-property class, not a record) —
/// same convention this codebase already uses for an Application-layer read result.
///
/// <see cref="IsPreliminary"/> is true whenever there is anything to show at all
/// (<see cref="HasComparableListings"/>): even with many comparables, this stays an asking-
/// price estimate, never a confirmed sale price (see <see cref="Disclaimer"/>) — Stage 4's
/// office responses are what eventually produce a Final Valuation, not this query.
/// <see cref="RequiresOfficeValuation"/> is the separate, narrower signal that hands off to
/// that later stage: true whenever there are fewer than 3 comparables (0, 1 or 2), including
/// the zero case where there is no valuation to show at all.
/// </summary>
public sealed class ComparableListingsResult
{
    public Guid InquiryId { get; set; }

    public bool HasComparableListings { get; set; }

    public int ComparableCount { get; set; }

    public decimal? MinPrice { get; set; }

    public decimal? MaxPrice { get; set; }

    public decimal? AveragePrice { get; set; }

    public decimal? MedianPrice { get; set; }

    /// <summary>Always true when <see cref="HasComparableListings"/> is true — see the class doc comment.</summary>
    public bool IsPreliminary { get; set; }

    /// <summary>True for 0, 1 or 2 comparables — signals the caller to proceed to Stage 4
    /// (office valuation). This method never triggers Stage 4 itself — see the module's
    /// final report for why that boundary is deliberate.</summary>
    public bool RequiresOfficeValuation { get; set; }

    /// <summary>Which location tier the search actually matched on — decided from which of
    /// the inquiry's Neighborhood/District/Governorate fields was available, not computed
    /// per-listing (every comparable in one result shares the same match level).</summary>
    public ValuationMatchLevel MatchLevel { get; set; }

    /// <summary>Fixed label identifying where this estimate comes from — never blank when
    /// <see cref="HasComparableListings"/> is true.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Fixed warning that these are asking prices, not confirmed transaction prices — always
    /// present whenever <see cref="HasComparableListings"/> is true. No backend
    /// localization/resources system exists in this project today (checked before adding
    /// this), so — consistent with how every other user-facing Application-layer message in
    /// this codebase is written (DomainException/ConflictException text, etc.) — this is a
    /// plain literal, not a resource-key lookup.
    /// </summary>
    public string? Disclaimer { get; set; }

    /// <summary>
    /// Extra context shown alongside the estimate: explains a limited-data preliminary result
    /// (1-2 comparables) or that no comparables exist yet (0). Null when there are 3+
    /// comparables — nothing extra needs to be said there.
    /// </summary>
    public string? Message { get; set; }
}
