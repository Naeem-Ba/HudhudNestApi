using System.Text.Json;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Admin.DTOs;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Plans.Interfaces;
using PropertyApi.Application.Users;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Domain.Audit.Constants;
using PropertyApi.Domain.Users;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Admin.Services;

/// <summary>
/// Admin-only plan/subscription lifecycle — see IAdminSubscriptionService's doc comment.
/// Follows the same shape as ConfirmFeaturedListingPaymentCommandHandler: validate, begin
/// tx, acquire an advisory lock (UserSubscriptionLock — two admins racing the same account
/// must not both compute the same base date), mutate the domain entity, save, commit, then
/// audit-log (same JSON-snapshot convention as AdminIdentityService.LogRoleChangeAsync,
/// since AuditLog has no dedicated targetUserId/reason columns). Lives here, not in
/// Infrastructure, because it depends only on repository/unit-of-work interfaces — same
/// reasoning as SelectPlanCommandHandler.
/// </summary>
public sealed class AdminSubscriptionService : IAdminSubscriptionService
{
    private readonly IUserAccountRepository _accounts;
    private readonly IPlanRepository _plans;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogs;
    private readonly ILogger<AdminSubscriptionService> _logger;

    public AdminSubscriptionService(
        IUserAccountRepository accounts,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IAuditLogService auditLogs,
        ILogger<AdminSubscriptionService> logger)
    {
        _accounts = accounts;
        _plans = plans;
        _unitOfWork = unitOfWork;
        _auditLogs = auditLogs;
        _logger = logger;
    }

    public async Task<AdminOperationResult> ActivatePlanAsync(
        Guid userId,
        string tier,
        int durationDays,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        if (!IsValidDuration(durationDays, out var durationError))
        {
            return AdminOperationResult.BadRequest(durationError);
        }

        var account = await _accounts.GetByIdAsync(userId, ct);
        if (account is null)
        {
            return AdminOperationResult.UserNotFound();
        }

        var plan = await _plans.GetByTierAsync(tier, ct);
        if (plan is null)
        {
            return AdminOperationResult.BadRequest($"'{tier}' is not a known, active plan.");
        }

        var oldSnapshot = SnapshotSubscription(account);

        var now = DateTime.UtcNow;
        var expiresAt = now.AddDays(durationDays);

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireAdvisoryLockAsync(UserSubscriptionLock.ForUser(userId), ct);

            account.ActivatePlanByAdmin(plan.Id, expiresAt, performedByUserId, now);

            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }

        await LogAsync(
            AuditActions.PlanActivatedByAdmin,
            performedByUserId,
            ipAddress,
            userId,
            reason,
            oldSnapshot,
            SnapshotSubscription(account),
            ct);

        _logger.LogInformation(
            "Plan activated by admin. UserId={UserId}, Tier={Tier}, ExpiresAt={ExpiresAt}, ActivatedBy={ActivatedBy}",
            userId, tier, expiresAt, performedByUserId);

        return AdminOperationResult.Ok($"Plan '{tier}' activated until {expiresAt:O}.");
    }

    public async Task<AdminOperationResult> ExtendSubscriptionAsync(
        Guid userId,
        int days,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        if (!IsValidDuration(days, out var durationError))
        {
            return AdminOperationResult.BadRequest(durationError);
        }

        var account = await _accounts.GetByIdAsync(userId, ct);
        if (account is null)
        {
            return AdminOperationResult.UserNotFound();
        }

        if (account.PlanId is null)
        {
            return AdminOperationResult.ConflictResult(
                "This account has never selected a plan — activate one first.");
        }

        var oldSnapshot = SnapshotSubscription(account);

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireAdvisoryLockAsync(UserSubscriptionLock.ForUser(userId), ct);

            account.ExtendPlan(TimeSpan.FromDays(days), performedByUserId, DateTime.UtcNow);

            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }

        await LogAsync(
            AuditActions.PlanExtendedByAdmin,
            performedByUserId,
            ipAddress,
            userId,
            reason,
            oldSnapshot,
            SnapshotSubscription(account),
            ct);

        _logger.LogInformation(
            "Subscription extended by admin. UserId={UserId}, Days={Days}, NewExpiresAt={ExpiresAt}, ExtendedBy={ExtendedBy}",
            userId, days, account.PlanExpiresAt, performedByUserId);

        return AdminOperationResult.Ok($"Subscription extended to {account.PlanExpiresAt:O}.");
    }

    public async Task<AdminOperationResult> CancelSubscriptionAsync(
        Guid userId,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var account = await _accounts.GetByIdAsync(userId, ct);
        if (account is null)
        {
            return AdminOperationResult.UserNotFound();
        }

        if (account.PlanId is null)
        {
            return AdminOperationResult.ConflictResult(
                "This account has never selected a plan — there is nothing to cancel.");
        }

        var oldSnapshot = SnapshotSubscription(account);

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireAdvisoryLockAsync(UserSubscriptionLock.ForUser(userId), ct);

            account.CancelPlan(performedByUserId, DateTime.UtcNow);

            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }

        await LogAsync(
            AuditActions.PlanCancelledByAdmin,
            performedByUserId,
            ipAddress,
            userId,
            reason,
            oldSnapshot,
            SnapshotSubscription(account),
            ct);

        _logger.LogInformation(
            "Subscription cancelled by admin. UserId={UserId}, CancelledBy={CancelledBy}",
            userId, performedByUserId);

        return AdminOperationResult.Ok("Subscription cancelled.");
    }

    private static bool IsValidDuration(int days, out string error)
    {
        if (days < SubscriptionLifecyclePolicy.MinAdminDurationDays
            || days > SubscriptionLifecyclePolicy.MaxAdminDurationDays)
        {
            error =
                $"Duration must be between {SubscriptionLifecyclePolicy.MinAdminDurationDays} " +
                $"and {SubscriptionLifecyclePolicy.MaxAdminDurationDays} days.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static object SnapshotSubscription(UserAccount account) => new
    {
        planId = account.PlanId,
        planStatus = account.PlanStatus.ToString(),
        planExpiresAt = account.PlanExpiresAt,
        planActivationSource = account.PlanActivationSource?.ToString(),
        planCancelledAt = account.PlanCancelledAt
    };

    private Task LogAsync(
        string action,
        Guid performedByUserId,
        string? ipAddress,
        Guid targetUserId,
        string? reason,
        object oldValue,
        object newValue,
        CancellationToken ct)
        => _auditLogs.LogAsync(
            userId: performedByUserId,
            action: action,
            ipAddress: ipAddress,
            oldValue: JsonSerializer.Serialize(new { targetUserId, snapshot = oldValue }),
            newValue: JsonSerializer.Serialize(new
            {
                targetUserId,
                snapshot = newValue,
                reason,
                timestamp = DateTime.UtcNow
            }),
            ct: ct);
}
