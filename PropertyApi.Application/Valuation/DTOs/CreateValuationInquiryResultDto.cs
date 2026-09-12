using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Valuation.DTOs;

/// <summary>
/// Stage 7 — the customer-facing result of submitting a valuation request: the Fast Path's
/// own <see cref="ComparableListingsResult"/> (Phase 3) unchanged, plus what happened next
/// (skip office matching entirely, or how many offices were actually reached and how).
///
/// Mirrors ComparableListingsResult's own settable-property-class convention (a compound
/// "Result" object, not a per-row list DTO) rather than a record — same shape as
/// OfficeMatchingResult.
/// </summary>
public sealed class CreateValuationInquiryResultDto
{
    public Guid InquiryId { get; set; }

    public ValuationInquiryStatus Status { get; set; }

    public DateTime ExpiresAt { get; set; }

    /// <summary>The Fast Path result computed for this inquiry — never null.</summary>
    public ComparableListingsResult FastPath { get; set; } = null!;

    /// <summary>Same flag as FastPath.RequiresOfficeValuation, duplicated at the top level
    /// purely so a caller never has to reach into FastPath to decide which of the fields
    /// below are meaningful.</summary>
    public bool RequiresOfficeValuation { get; set; }

    // ── Office Path fields — only meaningful when RequiresOfficeValuation is true;
    // left at their defaults (0/false) otherwise. ─────────────────────────────

    public int OfficeMatchCount { get; set; }

    public bool HasMinimumOfficeCoverage { get; set; }

    /// <summary>Stage 4's own "explicit and final" signal — surfaced immediately to the
    /// customer rather than only discovered 24h later when the SLA sweep closes the inquiry.</summary>
    public bool InsufficientOfficeCoverage { get; set; }

    /// <summary>
    /// The Nearby-Governorate disclosure rule: true whenever at least one matched office came
    /// from Level 4 (a neighboring governorate, not the inquiry's own) — the customer must be
    /// told their search was widened beyond their own governorate, since an office that far
    /// away may not be who they expected to hear from.
    /// </summary>
    public bool MatchedNeighboringGovernorate { get; set; }
}
