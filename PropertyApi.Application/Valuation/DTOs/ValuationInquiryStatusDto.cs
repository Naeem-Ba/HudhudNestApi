using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Valuation.DTOs;

/// <summary>One office's submitted estimate, as shown back to the customer. Deliberately
/// carries no agency identity — which office it came from is not this stage's concern to
/// expose (see ValuationOfficeInvitationDashboardDto's own privacy doc comment for the
/// mirror-image rule on the office's side).</summary>
public sealed record ValuationEstimateDto(
    decimal EstimatedPrice,
    string? Notes,
    DateTime SubmittedAt,
    ValuationMatchLevel MatchLevel);

/// <summary>Stage 7 — the customer's own "check my request" poll result.</summary>
public sealed class ValuationInquiryStatusDto
{
    public Guid InquiryId { get; set; }

    public ValuationInquiryStatus Status { get; set; }

    public DateTime ExpiresAt { get; set; }

    public IReadOnlyList<ValuationEstimateDto> Estimates { get; set; } = Array.Empty<ValuationEstimateDto>();
}
