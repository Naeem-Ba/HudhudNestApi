namespace HudhudNestApi.Application.Agencies.Interfaces;

/// <summary>
/// The two preconditions a real-estate office imposes on the person acting, enforced in the
/// API rather than in the client: a verified account, and — for the owner — a paid plan in
/// force. Both throw ForbiddenException carrying a stable code the client maps to its own
/// localised message (same contract as CreatePropertyCommandHandler's
/// LISTING_CONTACT_NOT_CONFIRMED / LISTING_PLAN_REQUIRED).
///
/// Deliberately a policy object rather than a check copied into each handler: "may this user
/// run an agency action" is asked by agency creation, by invitation creation and by accepting
/// an invitation, and the three must not be able to drift apart.
/// </summary>
public interface IAgencyEligibilityPolicy
{
    /// <summary>
    /// Throws 403 AGENCY_ACCOUNT_NOT_VERIFIED unless the account has confirmed a way to
    /// reach the person behind it — email OR phone, either is enough, exactly the
    /// definition of a verified account the listing gate already uses.
    /// </summary>
    Task EnsureVerifiedAccountAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Throws 403 AGENCY_PAID_PLAN_REQUIRED unless the account holds a paid plan whose
    /// benefits are in force right now: a plan was selected, it is neither cancelled nor
    /// expired (UserAccount.HasActivePlanBenefits), and its tier is not the free one.
    /// </summary>
    Task EnsurePaidPlanAsync(Guid userId, CancellationToken ct = default);
}
