namespace HudhudNestApi.Domain.Services.Enums;

/// <summary>
/// The ServiceRequest lifecycle. Transitions are enforced exclusively inside
/// <see cref="Entities.ServiceRequest"/> (never in a controller or handler) via
/// EnsureStatus-guarded methods, mirroring VisitRequest's state machine.
///
/// A conversation starts as Inquiry: the customer and the provider talk inside the app and nothing counts
/// as a request yet. The customer's explicit confirmation moves it to UnderReview (and stamps ConfirmedAt).
/// Rows created before the Inquiry stage existed were backfilled as confirmed.
///
/// Submitted → UnderReview happens instantaneously inside ServiceRequest.Create — the MVP is
/// single-provider-per-category, so there is no real routing/triage queue step yet that would
/// hold a request at "Submitted" for a meaningful duration. A request is created directly at
/// UnderReview; the audit trail (ServiceRequestStatusHistory) still records this as a real
/// transition from the true starting point.
///
/// Inquiry     → UnderReview (customer confirms) | Rejected | Cancelled | Expired
/// UnderReview → Accepted | Rejected | Cancelled | Expired
/// Accepted    → Scheduled | Cancelled | Expired
/// Scheduled   → InProgress | Cancelled | Expired
/// InProgress  → Completed | Cancelled
/// Completed   → Reviewed (set by AddServiceReview once, never a manual transition)
///
/// The provider can close a request as done from Accepted, Scheduled or InProgress (Schedule and
/// Start are optional bookkeeping, not gates), or as NotCompleted from the same three states with
/// a structured reason. NotCompleted is final, like Rejected and Cancelled.
/// </summary>
public enum ServiceRequestStatus
{
    Submitted = 0,
    UnderReview = 1,
    Accepted = 2,
    Scheduled = 3,
    InProgress = 4,
    Completed = 5,
    Reviewed = 6,
    Rejected = 7,
    Cancelled = 8,

    /// <summary>The provider closed an accepted request as not delivered (reason on the request).</summary>
    NotCompleted = 9,

    /// <summary>A conversation the customer has not confirmed as a request (yet). Counted as an inquiry only.</summary>
    Inquiry = 10,

    /// <summary>Closed by the system after a month without contact. Final, and kept for the statistics.</summary>
    Expired = 11,
}
