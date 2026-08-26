using MediatR;

namespace PropertyApi.Application.Users.Commands.SelectPlan;

/// <summary>
/// Records the caller's explicit plan choice — including "free", which is a real
/// selection here, not an implicit default (see UserAccount.PlanId doc comment).
/// </summary>
public sealed record SelectPlanCommand(Guid UserId, string Tier) : IRequest<bool>;
