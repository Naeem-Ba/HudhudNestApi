using HudhudNestApi.Domain.Valuation.Enums;

namespace HudhudNestApi.Application.Valuation.DTOs;

/// <summary>
/// One office's submitted estimate, as shown back to the customer.
///
/// Stage 9 update: now carries InvitationId, AgencyId and AgencyName — Stage 7's original
/// version deliberately carried none of these ("which office it came from is not this stage's
/// concern to expose"), but Stage 9 requires the customer to be able to pick ONE specific
/// office's estimate to consent to sharing contact info with
/// (SubmitValuationContactConsentCommand takes exactly this InvitationId). This is not a
/// reversal of this module's actual privacy rule: that rule has only ever run in one direction
/// — the OFFICE must not learn the CUSTOMER's identity/contact before consent (see
/// ValuationOfficeInvitationDashboardDto's own doc comment) — never the other way around. An
/// agency's name is not private to begin with: every Agency already has its own public profile
/// page (see Agency's own doc comment, "/agencies/{slug}"), so showing it here reveals nothing
/// an anonymous visitor to that page could not already see.
/// </summary>
public sealed record ValuationEstimateDto(
    Guid InvitationId,
    Guid AgencyId,
    string AgencyName,
    decimal EstimatedPrice,
    string? Notes,
    DateTime SubmittedAt,
    ValuationMatchLevel MatchLevel,

    /// <summary>True once SubmitValuationContactConsentCommand has already succeeded for this
    /// invitation — lets the customer's own UI show "contact info already shared" instead of
    /// offering the consent action again.</summary>
    bool ContactConsentGiven);

/// <summary>Stage 7 — the customer's own "check my request" poll result.</summary>
public sealed class ValuationInquiryStatusDto
{
    public Guid InquiryId { get; set; }

    public ValuationInquiryStatus Status { get; set; }

    public DateTime ExpiresAt { get; set; }

    public IReadOnlyList<ValuationEstimateDto> Estimates { get; set; } = Array.Empty<ValuationEstimateDto>();
}
