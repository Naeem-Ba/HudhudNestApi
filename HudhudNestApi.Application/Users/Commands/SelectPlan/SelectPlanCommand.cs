using MediatR;

namespace HudhudNestApi.Application.Users.Commands.SelectPlan;

/// <summary>
/// Records the caller's explicit plan choice — including "free", which is a real
/// selection here, not an implicit default (see UserAccount.PlanId doc comment).
///
/// The free plan takes effect immediately; any paid plan only becomes a request that an
/// administrator activates after confirming payment (see <see cref="SelectPlanResult"/>).
/// </summary>
public sealed record SelectPlanCommand(Guid UserId, string Tier) : IRequest<SelectPlanResult>;
