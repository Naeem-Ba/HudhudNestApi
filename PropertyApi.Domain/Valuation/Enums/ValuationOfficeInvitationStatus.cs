namespace PropertyApi.Domain.Valuation.Enums;

/// <summary>
/// Lifecycle of a <see cref="Entities.ValuationOfficeInvitation"/> sent to one agency for one
/// inquiry. Searched the rest of the Domain first (AgencyInvitationStatus, ServiceRequestStatus,
/// BookingStatus, VisitStatus, ...) — every feature module here defines its own status enum
/// rather than sharing one across unrelated entities, so this follows the same convention
/// instead of repurposing e.g. AgencyInvitationStatus (which models a *membership* invitation,
/// a different concept entirely).
///
/// Sent is the only state anything transitions OUT of — Responded and Expired are terminal.
/// </summary>
public enum ValuationOfficeInvitationStatus
{
    /// <summary>Invitation created and sent to the office; awaiting a response.</summary>
    Sent = 0,

    /// <summary>The office submitted a <see cref="Entities.ValuationOfficeResponse"/>.</summary>
    Responded = 1,

    /// <summary>The invitation's window elapsed with no response.</summary>
    Expired = 2,
}
