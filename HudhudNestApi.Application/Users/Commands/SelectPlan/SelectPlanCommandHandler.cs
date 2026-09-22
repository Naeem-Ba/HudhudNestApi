using MediatR;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Plans.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;

namespace HudhudNestApi.Application.Users.Commands.SelectPlan;

/// <summary>
/// Returns false (rather than throwing) only when the caller's own identity/account
/// cannot be resolved — the controller turns that into 401, matching
/// UpdateUserCommandHandler's convention. An unknown/inactive tier is the caller's
/// own input being wrong, so it goes through ValidationException (422) instead.
/// </summary>
public sealed class SelectPlanCommandHandler : IRequestHandler<SelectPlanCommand, bool>
{
    private readonly IUserIdentityReadService _identity;
    private readonly IUserAccountRepository _accounts;
    private readonly IPlanRepository _plans;
    private readonly IUnitOfWork _unitOfWork;

    public SelectPlanCommandHandler(
        IUserIdentityReadService identity,
        IUserAccountRepository accounts,
        IPlanRepository plans,
        IUnitOfWork unitOfWork)
    {
        _identity = identity;
        _accounts = accounts;
        _plans = plans;
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

        account.SelectPlan(plan.Id, DateTime.UtcNow);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
