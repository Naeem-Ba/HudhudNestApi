using MediatR;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Plans.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Plans.Constants;
using HudhudNestApi.Domain.Users;

namespace HudhudNestApi.Application.Users.Commands.SelectPlan;

/// <summary>
/// Returns AccountNotFound (rather than throwing) only when the caller's own identity/account
/// cannot be resolved — the controller turns that into 401, matching
/// UpdateUserCommandHandler's convention. An unknown/inactive tier is the caller's
/// own input being wrong, so it goes through ValidationException (422) instead.
/// </summary>
public sealed class SelectPlanCommandHandler : IRequestHandler<SelectPlanCommand, SelectPlanResult>
{
    /// <summary>Stable code the client maps to its own localised message.</summary>
    public const string AgencyFreePlanNotAllowedCode = "AGENCY_FREE_PLAN_NOT_ALLOWED";

    private readonly IUserIdentityReadService _identity;
    private readonly IUserAccountRepository _accounts;
    private readonly IPlanRepository _plans;
    private readonly IPlanRequestRepository _planRequests;
    private readonly IAgencyRepository _agencies;
    private readonly IUnitOfWork _unitOfWork;

    public SelectPlanCommandHandler(
        IUserIdentityReadService identity,
        IUserAccountRepository accounts,
        IPlanRepository plans,
        IPlanRequestRepository planRequests,
        IAgencyRepository agencies,
        IUnitOfWork unitOfWork)
    {
        _identity = identity;
        _accounts = accounts;
        _plans = plans;
        _planRequests = planRequests;
        _agencies = agencies;
        _unitOfWork = unitOfWork;
    }

    public async Task<SelectPlanResult> Handle(SelectPlanCommand request, CancellationToken cancellationToken)
    {
        var identity = await _identity.FindByIdAsync(request.UserId, cancellationToken);
        if (identity is null || identity.IsDeleted)
        {
            return new SelectPlanResult(SelectPlanOutcome.AccountNotFound);
        }

        var account = await _accounts.GetByIdAsync(identity.UserAccountId, cancellationToken);
        if (account is null)
        {
            return new SelectPlanResult(SelectPlanOutcome.AccountNotFound);
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

        var now = DateTime.UtcNow;

        if (string.Equals(plan.Tier, PlanTiers.Free, StringComparison.OrdinalIgnoreCase))
        {
            account.SelectPlan(plan.Id, now);

            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new SelectPlanResult(SelectPlanOutcome.Activated, plan.Tier);
        }

        // A paid plan is never activated by the person asking for it: there is no payment
        // gateway, so activating here would make every paid tier free for anyone who calls
        // this endpoint. The request is recorded instead and the account keeps whatever plan
        // it has now (a pending request grants nothing and does not count towards the paid
        // plan an office needs). An administrator activates it once payment is confirmed.
        //
        // One outstanding request per user: asking again replaces the earlier one rather than
        // piling up, so the queue an administrator works through never holds two rows for
        // the same person.
        // Serialized per user on the same lock the admin subscription actions take
        // (UserSubscriptionLock): a double tap, or a request racing an admin's decision,
        // must not trip the one-Pending-per-user unique index and surface as a 500.
        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            await _unitOfWork.AcquireAdvisoryLockAsync(
                UserSubscriptionLock.ForUser(account.Id), cancellationToken);

            var pending = await _planRequests.GetPendingForUserAsync(account.Id, cancellationToken);

            if (pending is not null && pending.PlanId == plan.Id)
            {
                await _unitOfWork.CommitTransactionAsync(cancellationToken);
                return new SelectPlanResult(SelectPlanOutcome.PendingAdminApproval, plan.Tier, pending.Id);
            }

            // The superseded row is written before the new Pending row exists, or the
            // unique index could see two Pending rows for an instant.
            if (pending is not null)
            {
                pending.Supersede(now);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var created = Domain.Plans.Entities.PlanRequest.Create(account.Id, plan.Id, now);
            await _planRequests.AddAsync(created, cancellationToken);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            return new SelectPlanResult(SelectPlanOutcome.PendingAdminApproval, plan.Tier, created.Id);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
