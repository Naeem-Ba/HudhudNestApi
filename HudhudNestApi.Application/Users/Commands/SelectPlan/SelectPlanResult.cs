namespace HudhudNestApi.Application.Users.Commands.SelectPlan;

public enum SelectPlanOutcome
{
    /// <summary>The caller's own identity/account could not be resolved (the controller answers 401).</summary>
    AccountNotFound = 0,

    /// <summary>A free plan: nothing to pay, so it is in force immediately.</summary>
    Activated = 1,

    /// <summary>
    /// A paid plan: recorded as a request that grants nothing until an administrator
    /// confirms payment and activates it.
    /// </summary>
    PendingAdminApproval = 2,
}

/// <param name="Outcome">What happened.</param>
/// <param name="Tier">The tier that was asked for (normalised).</param>
/// <param name="RequestId">The pending request's id, only for <see cref="SelectPlanOutcome.PendingAdminApproval"/>.</param>
public sealed record SelectPlanResult(SelectPlanOutcome Outcome, string? Tier = null, Guid? RequestId = null);
