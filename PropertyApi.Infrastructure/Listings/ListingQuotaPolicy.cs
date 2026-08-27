using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Plans.Interfaces;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Infrastructure.Listings;

/// <summary>
/// Plan-driven implementation of <see cref="IListingQuotaPolicy"/> — see that interface's
/// doc comment for the agency-owner-plan semantics this enforces (BACKEND-ISSUES.md §B-3).
///
/// Deliberately reads through IPlanRepository/IAgencyRepository on every call rather than
/// caching: a listing creation is not a hot path (it goes through an advisory-lock-guarded
/// transaction already — see ListingQuotaLock), and a plan change or ownership transfer must
/// take effect on the very next attempt, not after some cache TTL expires.
/// </summary>
internal sealed class ListingQuotaPolicy : IListingQuotaPolicy
{
    private readonly IPlanRepository _plans;
    private readonly IAgencyRepository _agencies;

    public ListingQuotaPolicy(IPlanRepository plans, IAgencyRepository agencies)
    {
        _plans = plans;
        _agencies = agencies;
    }

    public async Task<int> GetActiveListingLimitAsync(
        UserAccount ownerAccount,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ownerAccount);

        if (ownerAccount.AgencyId is { } agencyId)
        {
            return await ResolveAgencyLimitAsync(agencyId, ct);
        }

        return await ResolvePlanLimitAsync(
            ownerAccount.PlanId,
            "اختر خطة أولاً لتحديد حصة الإعلانات المسموح بها.",
            ct);
    }

    /// <summary>
    /// The agency's pooled limit comes from its OWNER's plan, never from the plan of the
    /// member who happens to be creating the listing right now — see IListingQuotaPolicy's
    /// doc comment for why. Agency has no PlanId of its own (by design — see Agency's doc
    /// comment on why it stays a thin organisational grouping), so this reads the owner's
    /// UserAccount the same way CreatePropertyCommandHandler already reads any owner's.
    /// </summary>
    private async Task<int> ResolveAgencyLimitAsync(Guid agencyId, CancellationToken ct)
    {
        var agency = await _agencies.GetByIdAsync(agencyId, ct)
            ?? throw new ConflictException(
                "تعذّر تحديد حصة الإعلانات لمكتبكم — المكتب غير موجود.");

        var owner = await _agencies.GetUserAccountAsync(agency.OwnerUserId, ct)
            ?? throw new ConflictException(
                "تعذّر تحديد حصة الإعلانات لمكتبكم — تعذّر العثور على حساب مالك المكتب.");

        return await ResolvePlanLimitAsync(
            owner.PlanId,
            "لم يختر مالك مكتبكم خطة بعد، فلا يمكن تحديد حصة الإعلانات المسموح بها للمكتب. " +
            "تواصلوا مع مالك المكتب لاختيار خطة.",
            ct);
    }

    /// <summary>
    /// Resolves Plan.ListingLimit for a given PlanId. <paramref name="noPlanMessage"/> lets
    /// the caller phrase "no plan" for the two different subjects it can mean (the acting
    /// owner themselves, or their agency's owner) without duplicating the resolution logic.
    ///
    /// Never returns a bypass for a state it cannot explain: null PlanId, or a PlanId that no
    /// longer resolves to a Plan row (should not happen — UserAccount.PlanId has a Restrict
    /// FK to Plans specifically so a referenced Plan cannot be deleted out from under an
    /// account — but this is the one place a stale/orphaned reference could otherwise turn
    /// into an accidental unlimited quota, so it is checked anyway) both reject rather than
    /// silently allow.
    /// </summary>
    private async Task<int> ResolvePlanLimitAsync(
        Guid? planId,
        string noPlanMessage,
        CancellationToken ct)
    {
        if (planId is not { } id)
        {
            throw new ConflictException(noPlanMessage);
        }

        var plan = await _plans.GetByIdAsync(id, ct)
            ?? throw new ConflictException("تعذّر العثور على الخطة المرتبطة بالحساب.");

        // Null Plan.ListingLimit means unlimited (Elite's negotiated quota) — represented as
        // int.MaxValue so every caller can keep comparing "active < limit" without a second
        // branch for "unlimited".
        return plan.ListingLimit ?? int.MaxValue;
    }
}
