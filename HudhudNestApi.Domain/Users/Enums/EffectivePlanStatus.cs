namespace HudhudNestApi.Domain.Users.Enums;

/// <summary>
/// The subscription status an admin (or the quota policy) actually cares about — computed
/// on demand by <see cref="Entities.UserAccount.GetEffectivePlanStatus"/> from the stored
/// <see cref="PlanStatus"/> plus <c>PlanExpiresAt</c>, never persisted itself. Kept separate
/// from <see cref="PlanStatus"/> so "expired" never has to be swept/written by a background
/// job — it's true the instant <c>PlanExpiresAt</c> passes, the same way
/// <c>Property.IsCurrentlyFeatured(asOfUtc)</c> is computed rather than flipped by a sweep.
/// </summary>
public enum EffectivePlanStatus
{
    /// <summary>UserAccount.PlanId is null — no plan ever selected.</summary>
    NoPlan = 0,

    /// <summary>PlanStatus == Active and (no expiry, or expiry is still in the future).</summary>
    Active = 1,

    /// <summary>PlanStatus == Active but PlanExpiresAt has passed.</summary>
    Expired = 2,

    /// <summary>PlanStatus == Cancelled.</summary>
    Cancelled = 3,
}
