namespace HudhudNestApi.Domain.Services.Enums;

/// <summary>
/// The ServiceRequest lifecycle. Transitions are enforced exclusively inside
/// <see cref="Entities.ServiceRequest"/> (never in a controller or handler) via
/// EnsureStatus-guarded methods, mirroring VisitRequest's state machine.
///
/// Submitted → UnderReview happens instantaneously inside ServiceRequest.Create — the MVP is
/// single-provider-per-category, so there is no real routing/triage queue step yet that would
/// hold a request at "Submitted" for a meaningful duration. A request is created directly at
/// UnderReview; the audit trail (ServiceRequestStatusHistory) still records this as a real
/// transition from the true starting point.
///
/// UnderReview → Accepted | Rejected | Cancelled
/// Accepted    → Scheduled | Cancelled
/// Scheduled   → InProgress | Cancelled
/// InProgress  → Completed | Cancelled
/// Completed   → Reviewed (set by AddServiceReview once, never a manual transition)
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
}
