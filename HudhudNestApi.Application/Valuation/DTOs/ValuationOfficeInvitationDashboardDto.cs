using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Valuation.Enums;

namespace HudhudNestApi.Application.Valuation.DTOs;

/// <summary>
/// One row of Stage 6's office dashboard (GetMyAgencyValuationInquiriesQuery) — an invitation
/// sent to the caller's own agency, flattened together with its parent inquiry's descriptive
/// fields and (if the office already responded) its own submitted estimate.
///
/// Deliberately carries no more of the inquiry than its property description — same
/// "authenticated party sees only what it needs, not the whole aggregate" discipline
/// AgencyInvitationDto's own doc comment applies to its Agency reference. In particular this
/// NEVER exposes ValuationInquiry.RequesterId — the office is not entitled to know WHO is
/// asking, ever (that identity itself is never shared, consent or not — only contact details
/// are, see below).
///
/// Stage 9 update: ContactPhone/ContactEmail were added below, and they are the ONE place this
/// DTO is allowed to carry customer data — but only conditionally. GetMyAgencyValuationInquiriesQuery
/// populates them from a ValuationContactConsent row if (and only if) one exists for this exact
/// invitation; otherwise both stay null. This replaces Stage 6's original structural guard
/// ("this DTO can never have these properties at all" — see
/// ValuationOfficeInvitationDashboardDtoTests' own updated doc comment) with a behavioural one
/// ("these properties exist, but are null pre-consent") — the whole point of this stage.
/// </summary>
public sealed record ValuationOfficeInvitationDashboardDto(
    Guid InvitationId,
    Guid InquiryId,
    ValuationMatchLevel MatchLevel,
    ValuationOfficeInvitationStatus InvitationStatus,
    DateTime SentAt,
    DateTime? RespondedAt,

    /// <summary>
    /// True only when Status is still Sent AND the inquiry's own ExpiresAt has not yet
    /// passed — computed fresh at read time from the same IsExpired(utcNow, expiresAt) check
    /// SubmitOfficeResponseCommandHandler itself enforces, not merely read off Status. Status
    /// can lag up to ValuationInquiryExpiryHostedService's 15-minute sweep interval behind
    /// reality; a UI that only disabled its "respond" button when Status said Expired would
    /// show a stale, briefly-wrong affordance. The backend still re-checks independently on
    /// submit regardless of what this flag says — this exists for the UI, not as the
    /// authorization boundary.
    /// </summary>
    bool CanRespond,

    int GovernorateId,
    int? DistrictId,
    int? NeighborhoodId,
    int? PropertyTypeId,
    decimal? Area,
    int? Rooms,
    ListingType RequestType,
    ValuationInquiryStatus InquiryStatus,
    DateTime InquiryExpiresAt,

    decimal? MyEstimatedPrice,
    string? MyNotes,

    /// <summary>Null unless the customer has explicitly consented to share contact info for
    /// THIS invitation (see this record's own doc comment). Never populated from anything
    /// other than a ValuationContactConsent row — in particular, never derived from
    /// ValuationInquiry.RequesterId's linked account.</summary>
    string? CustomerContactPhone,
    string? CustomerContactEmail);
