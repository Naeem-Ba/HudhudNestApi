using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Plans.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Plans.Constants;

namespace HudhudNestApi.Application.Agencies.Services;

/// <summary>
/// See IAgencyEligibilityPolicy. Confirmation state is read from the Identity snapshot (the
/// source of truth — see IdentityAccountSnapshot) and plan state from UserAccount, never
/// from a role or a cached flag: roles travel inside an access token and would let a stale
/// token answer a question about the account's state right now.
/// </summary>
public sealed class AgencyEligibilityPolicy : IAgencyEligibilityPolicy
{
    public const string AccountNotVerifiedCode = "AGENCY_ACCOUNT_NOT_VERIFIED";
    public const string PaidPlanRequiredCode = "AGENCY_PAID_PLAN_REQUIRED";

    private readonly IUserIdentityReadService _identity;
    private readonly IUserAccountRepository _accounts;
    private readonly IPlanRepository _plans;

    public AgencyEligibilityPolicy(
        IUserIdentityReadService identity,
        IUserAccountRepository accounts,
        IPlanRepository plans)
    {
        _identity = identity;
        _accounts = accounts;
        _plans = plans;
    }

    public async Task EnsureVerifiedAccountAsync(Guid userId, CancellationToken ct = default)
    {
        var identity = await _identity.FindByIdAsync(userId, ct);

        if (identity is null || identity.IsDeleted)
            throw new NotFoundException("User account was not found.");

        if (identity.EmailConfirmed || identity.PhoneConfirmed)
            return;

        throw new ForbiddenException(
            "وثّق حسابك أولاً — أكّد بريدك الإلكتروني أو رقم هاتفك قبل إنشاء مكتب عقاري أو الانضمام إليه.",
            AccountNotVerifiedCode);
    }

    public async Task EnsurePaidPlanAsync(Guid userId, CancellationToken ct = default)
    {
        var identity = await _identity.FindByIdAsync(userId, ct);

        if (identity is null || identity.IsDeleted)
            throw new NotFoundException("User account was not found.");

        var account = await _accounts.GetByIdAsync(identity.UserAccountId, ct);

        if (account is null)
            throw new NotFoundException("User account was not found.");

        // No plan at all, or one that is cancelled/expired: HasActivePlanBenefits already
        // answers both (EffectivePlanStatus), so there is nothing to re-derive here.
        if (account.PlanId is { } planId && account.HasActivePlanBenefits(DateTime.UtcNow))
        {
            // GetByIdAsync is deliberately not filtered by IsActive (see IPlanRepository):
            // a tier retired from the catalog must not silently stop being paid for the
            // customer still on it.
            var plan = await _plans.GetByIdAsync(planId, ct);

            if (plan is not null &&
                !string.Equals(plan.Tier, PlanTiers.Free, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        throw new ForbiddenException(
            "المكتب العقاري يتطلب خطة مدفوعة سارية. الخطة المجانية لا تشمل المكاتب العقارية.",
            PaidPlanRequiredCode);
    }
}
