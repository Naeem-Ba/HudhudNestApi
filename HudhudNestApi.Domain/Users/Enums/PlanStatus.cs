namespace HudhudNestApi.Domain.Users.Enums;

/// <summary>
/// Persisted lifecycle flag for <see cref="Entities.UserAccount.PlanId"/>. Deliberately has
/// only two members — this is NOT the full state a caller usually wants (that's
/// <see cref="EffectivePlanStatus"/>, computed from this plus <c>PlanExpiresAt</c>):
///
/// - Active: the plan is in force. It may still be past its <c>PlanExpiresAt</c> — that
///   "time-based expiry while the stored flag still says Active" is exactly what
///   <see cref="EffectivePlanStatus.Expired"/> exists to describe, mirroring how
///   <c>PropertyStatus.Expired</c> is a distinct, time-driven state from an owner's
///   deliberate choice (see that enum's doc comment for the same pattern).
/// - Cancelled: an admin explicitly stopped the plan (UserAccount.CancelPlan). Terminal
///   until a new admin action (ActivatePlanByAdmin/ExtendPlan) moves it back to Active.
/// </summary>
public enum PlanStatus
{
    Active = 0,
    Cancelled = 1,
}
