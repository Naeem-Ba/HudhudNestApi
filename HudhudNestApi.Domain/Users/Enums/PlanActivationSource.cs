namespace HudhudNestApi.Domain.Users.Enums;

/// <summary>
/// Records how <see cref="Entities.UserAccount.PlanId"/> came to be set — shown to admins
/// on the user-detail screen ("مصدر التفعيل: دفع ذاتي أو تفعيل إداري") so a manually
/// granted plan is never mistaken for a paid one. Null on <c>UserAccount</c> until the
/// first selection/activation happens.
/// </summary>
public enum PlanActivationSource
{
    /// <summary>The user picked this plan themselves via POST /users/me/plan (/pricing).</summary>
    SelfService = 0,

    /// <summary>An admin activated/extended this plan manually — no payment involved.</summary>
    AdminGrant = 1,
}
