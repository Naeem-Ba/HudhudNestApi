namespace HudhudNestApi.Domain.Plans.Enums;

/// <summary>
/// Lifecycle of a user's request for a paid plan. Pending is the only state anything moves
/// OUT of. The numeric values are persisted and referenced by a filtered unique index
/// (one Pending request per user) — do not reorder.
/// </summary>
public enum PlanRequestStatus
{
    /// <summary>Asked for; payment not yet confirmed by an administrator. Grants nothing.</summary>
    Pending = 0,

    /// <summary>An administrator confirmed payment and activated the plan.</summary>
    Approved = 1,

    /// <summary>An administrator declined it (payment not received, wrong amount, …).</summary>
    Rejected = 2,

    /// <summary>The user asked for a different plan before this one was resolved.</summary>
    Superseded = 3,
}
