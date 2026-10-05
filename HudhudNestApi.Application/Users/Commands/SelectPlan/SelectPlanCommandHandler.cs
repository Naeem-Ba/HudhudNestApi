using MediatR;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Plans.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Plans.Constants;

namespace HudhudNestApi.Application.Users.Commands.SelectPlan;

/// <summary>
/// Returns false (rather than throwing) only when the caller's own identity/account
/// cannot be resolved — the controller turns that into 401, matching
/// UpdateUserCommandHandler's convention. An unknown/inactive tier is the caller's
/// own input being wrong, so it goes through ValidationException (422) instead.
/// </summary>
public sealed class SelectPlanCommandHandler : IRequestHandler<SelectPlanCommand, bool>
{
    /// <summary>Stable code the client maps to its own localised message.</summary>
    public const string AgencyFreePlanNotAllowedCode = "AGENCY_FREE_PLAN_NOT_ALLOWED";

    private readonly IUserIdentityReadService _identity;
    private readonly IUserAccountRepository _accounts;
    private readonly IPlanRepository _plans;
    private readonly IAgencyRepository _agencies;
    private readonly IUnitOfWork _unitOfWork;

    public SelectPlanCommandHandler(
        IUserIdentityReadService identity,
        IUserAccountRepository accounts,
        IPlanRepository plans,
        IAgencyRepository agencies,
        IUnitOfWork unitOfWork)
    {
        _identity = identity;
        _accounts = accounts;
        _plans = plans;
        _agencies = agencies;
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> Handle(SelectPlanCommand request, CancellationToken cancellationToken)
    {
        var identity = await _identity.FindByIdAsync(request.UserId, cancellationToken);
        if (identity is null || identity.IsDeleted)
        {
            return false;
        }

        var account = await _accounts.GetByIdAsync(identity.UserAccountId, cancellationToken);
        if (account is null)
        {
            return false;
        }

        var plan = await _plans.GetByTierAsync(request.Tier, cancellationToken);
        if (plan is null)
        {
            throw new ValidationException(
                nameof(SelectPlanCommand.Tier),
                $"'{request.Tier}' is not a known, active plan.",
                "PLAN_TIER_UNKNOWN");
        }

        // The free tier does not cover real-estate offices, so an agency owner cannot move
        // onto it: that would leave the office running on a plan it is not entitled to, and
        // silently shrink the whole agency's pooled listing quota to the free limit, since
        // the pool is resolved from the owner's plan (IListingQuotaPolicy). Blocked rather
        // than silently converted, so the owner learns why. Only the owner is checked — a
        // member's own tier governs nothing about the agency.
        if (string.Equals(plan.Tier, PlanTiers.Free, StringComparison.OrdinalIgnoreCase))
        {
            var ownedAgency = await _agencies.GetByOwnerAsync(request.UserId, cancellationToken);

            if (ownedAgency is { IsDeleted: false })
            {
                throw new ForbiddenException(
                    "تملك مكتباً عقارياً، والخطة المجانية لا تشمل المكاتب العقارية. اختر خطة مدفوعة.",
                    AgencyFreePlanNotAllowedCode);
            }
        }

        account.SelectPlan(plan.Id, DateTime.UtcNow);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
