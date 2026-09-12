using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Valuation.DTOs;

/// <summary>
/// One row of Stage 6's office dashboard (GetMyAgencyValuationInquiriesQuery) — an invitation
/// sent to the caller's own agency, flattened together with its parent inquiry's descriptive
/// fields and (if the office already responded) its own submitted estimate.
///
/// Deliberately carries no more of the inquiry than its property description — same
/// "authenticated party sees only what it needs, not the whole aggregate" discipline
/// AgencyInvitationDto's own doc comment applies to its Agency reference. In particular this
/// NEVER exposes ValuationInquiry.RequesterId or any requester contact info: the office is not
/// entitled to know who is asking until a customer explicitly consents to share that (a later
/// stage's job) — see ValuationInquiry's own RequesterId doc comment and this module's Stage 6
/// completion report.
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
    string? MyNotes);
