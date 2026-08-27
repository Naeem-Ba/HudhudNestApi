using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Listings.Interfaces;

/// <summary>
/// Resolves the maximum simultaneously-active listings an owner may hold, driven by
/// <c>Plan.ListingLimit</c> — the plan the owner explicitly selected
/// (<see cref="UserAccount.PlanId"/>) — rather than by any constant in configuration or in
/// <c>ListingLifecyclePolicy</c>. See BACKEND-ISSUES.md §B-3.
///
/// Agency semantics: when <paramref name="ownerAccount"/> belongs to an agency, the returned
/// limit is still pooled across every member's active listings together — not divided per
/// member — exactly as before this change (RELEASE-BLOCKERS-AR.md's original B-3 mitigation:
/// an agency owner decides internally which members actually publish, so the cap belongs to
/// the agency account as a whole). What changed is *which* plan sets that pooled number: the
/// agency OWNER's plan, not the plan of whichever member happens to be creating the listing
/// right now. Two things this rules out on purpose:
///   1. A member on a cheaper (or no) individual plan does not shrink the agency's pool —
///      the pool is not "each member's own limit, summed".
///   2. A member cannot raise the agency's pool by switching their own personal plan — only
///      the owner's plan choice moves the number, so one member cannot inflate a shared
///      resource everyone else in the office also draws from.
/// This mirrors how Agency itself works everywhere else in this codebase: the owner is the
/// only member who administers the agency (add/remove members, transfer ownership,
/// deactivate) — Agency has no PlanId of its own, so "the agency's plan" is read through its
/// owner rather than requiring a second, parallel plan-selection surface on the agency
/// itself.
/// </summary>
public interface IListingQuotaPolicy
{
    /// <summary>
    /// Resolves the maximum active listings <paramref name="ownerAccount"/> — or its agency,
    /// pooled, if it belongs to one — may hold. Returns <see cref="int.MaxValue"/> for an
    /// unlimited plan (Plan.ListingLimit == null).
    ///
    /// Throws <see cref="PropertyApi.Application.Common.Exceptions.ConflictException"/> when
    /// the limit cannot be safely resolved — no plan selected (owner or, for an agency
    /// member, the agency owner), an unresolvable plan/agency reference, etc. This never
    /// returns an unlimited/bypassed quota for a state it cannot explain: see the
    /// implementation's remarks for the exact cases.
    /// </summary>
    Task<int> GetActiveListingLimitAsync(
        UserAccount ownerAccount,
        CancellationToken ct = default);
}
