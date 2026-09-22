namespace HudhudNestApi.Domain.Valuation.Enums;

/// <summary>
/// Lifecycle of a <see cref="Entities.ValuationInquiry"/>.
///
/// Pending is the only entry state. From there the inquiry can pick up listing matches
/// and/or move on to inviting offices; Completed and Expired are both terminal — mirrors
/// the terminal/non-terminal split every other status-machine entity in this codebase uses
/// (AgencyInvitationStatus, ServiceRequestStatus, VisitStatus). See ValuationInquiry's
/// domain methods for exactly which transitions are allowed.
/// </summary>
public enum ValuationInquiryStatus
{
    /// <summary>Inquiry created; no listing or office matching has started yet.</summary>
    Pending = 0,

    /// <summary>Matching listings were found and can be used toward the valuation.</summary>
    MatchedFromListings = 1,

    /// <summary>Offices were matched and invited; waiting on their responses.</summary>
    AwaitingOfficeResponses = 2,

    /// <summary>The valuation process finished.</summary>
    Completed = 3,

    /// <summary>The inquiry's window elapsed before it was completed.</summary>
    Expired = 4,
}
