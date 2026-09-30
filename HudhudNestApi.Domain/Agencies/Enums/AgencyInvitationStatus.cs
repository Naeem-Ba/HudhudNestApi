namespace HudhudNestApi.Domain.Agencies.Enums;

/// <summary>
/// Lifecycle of an <see cref="Entities.AgencyInvitation"/>.
///
/// Pending is the only state anything can transition OUT of — Accepted, Declined and
/// Expired are all terminal. There is no "un-accept" or "un-decline": AgencyInvitation's
/// domain methods enforce that both directions, mirroring VisitRequest's state machine.
/// </summary>
public enum AgencyInvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Expired = 3,
}
