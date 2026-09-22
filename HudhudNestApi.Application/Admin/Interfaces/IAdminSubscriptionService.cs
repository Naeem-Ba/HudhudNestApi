using HudhudNestApi.Application.Admin.DTOs;

namespace HudhudNestApi.Application.Admin.Interfaces;

/// <summary>
/// Admin-only plan/subscription lifecycle actions — activate a paid plan for free, extend
/// it, or cancel it. Distinct from Users.Commands.SelectPlan (self-service, no expiry, no
/// audit trail): every method here writes an AuditLog entry and requires a resolvable
/// admin actor.
/// </summary>
public interface IAdminSubscriptionService
{
    Task<AdminOperationResult> ActivatePlanAsync(
        Guid userId,
        string tier,
        int durationDays,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default);

    Task<AdminOperationResult> ExtendSubscriptionAsync(
        Guid userId,
        int days,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default);

    Task<AdminOperationResult> CancelSubscriptionAsync(
        Guid userId,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default);
}
