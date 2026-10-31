using System.Text.Json;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Admin.DTOs;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Plans.Interfaces;
using HudhudNestApi.Application.Plans.Services;
using HudhudNestApi.Application.Users;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Plans.Enums;
using HudhudNestApi.Domain.Users;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Admin.Services;

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
    private readonly IPlanRequestRepository _planRequests;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogs;
    private readonly BillingNotifier _billing;
    private readonly ILogger<AdminSubscriptionService> _logger;

    public AdminSubscriptionService(
        IUserAccountRepository accounts,
        IPlanRepository plans,
        IPlanRequestRepository planRequests,
        IUnitOfWork unitOfWork,
        IAuditLogService auditLogs,
        BillingNotifier billing,
        ILogger<AdminSubscriptionService> logger)
    {
        _accounts = accounts;
        _plans = plans;
        _planRequests = planRequests;
        _unitOfWork = unitOfWork;
        _auditLogs = auditLogs;
        _billing = billing;
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

            // A plan the administrator grants directly settles whatever the user had asked for: left Pending, the old request
            // would sit in the queue and later overwrite this decision (QA Phase 13, P13-D06).
            var outstanding = await _planRequests.GetPendingForUserAsync(userId, ct);
            outstanding?.Supersede(now);

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

        if (account.PlanStatus == HudhudNestApi.Domain.Users.Enums.PlanStatus.Cancelled)
        {
            return AdminOperationResult.ConflictResult("This subscription is already cancelled.");
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

    public async Task<AdminOperationResult> ApprovePlanRequestAsync(
        Guid requestId,
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

        var planRequest = await _planRequests.GetByIdAsync(requestId, ct);
        if (planRequest is null)
        {
            return AdminOperationResult.NotFoundResult("Plan request was not found.");
        }

        if (planRequest.Status != PlanRequestStatus.Pending)
        {
            return AdminOperationResult.ConflictResult(
                $"This request is already {planRequest.Status}.");
        }

        var account = await _accounts.GetByIdAsync(planRequest.UserId, ct);
        if (account is null)
        {
            return AdminOperationResult.UserNotFound();
        }

        var plan = await _plans.GetByIdAsync(planRequest.PlanId, ct);
        if (plan is null)
        {
            return AdminOperationResult.BadRequest("The requested plan no longer exists.");
        }

        var oldSnapshot = SnapshotSubscription(account);

        var now = DateTime.UtcNow;
        var expiresAt = now.AddDays(durationDays);

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireAdvisoryLockAsync(UserSubscriptionLock.ForUser(account.Id), ct);

            // Re-read under the lock: another admin (or the user replacing the request) may
            // have resolved it while this one waited, and a request must grant a plan once.
            await _planRequests.ReloadAsync(planRequest, ct);
            if (planRequest.Status != PlanRequestStatus.Pending)
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                return AdminOperationResult.ConflictResult(
                    $"This request is already {planRequest.Status}.");
            }

            account.ActivatePlanByAdmin(plan.Id, expiresAt, performedByUserId, now);
            planRequest.Approve(performedByUserId, reason, now);

            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }

        await LogAsync(
            AuditActions.PlanRequestApprovedByAdmin,
            performedByUserId,
            ipAddress,
            account.Id,
            reason,
            oldSnapshot,
            SnapshotSubscription(account),
            ct);

        _logger.LogInformation(
            "Plan request approved. RequestId={RequestId}, UserId={UserId}, Tier={Tier}, ExpiresAt={ExpiresAt}, ApprovedBy={ApprovedBy}",
            requestId, account.Id, plan.Tier, expiresAt, performedByUserId);

        await _billing.PlanRequestApprovedAsync(account.Id, requestId, plan.Tier, expiresAt, ct);

        return AdminOperationResult.Ok($"Plan '{plan.Tier}' activated until {expiresAt:O}.");
    }

    public async Task<AdminOperationResult> RejectPlanRequestAsync(
        Guid requestId,
        string? reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var planRequest = await _planRequests.GetByIdAsync(requestId, ct);
        if (planRequest is null)
        {
            return AdminOperationResult.NotFoundResult("Plan request was not found.");
        }

        if (planRequest.Status != PlanRequestStatus.Pending)
        {
            return AdminOperationResult.ConflictResult(
                $"This request is already {planRequest.Status}.");
        }

        await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            await _unitOfWork.AcquireAdvisoryLockAsync(UserSubscriptionLock.ForUser(planRequest.UserId), ct);

            await _planRequests.ReloadAsync(planRequest, ct);
            if (planRequest.Status != PlanRequestStatus.Pending)
            {
                await _unitOfWork.RollbackTransactionAsync(ct);
                return AdminOperationResult.ConflictResult(
                    $"This request is already {planRequest.Status}.");
            }

            planRequest.Reject(performedByUserId, reason, DateTime.UtcNow);

            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }

        await LogAsync(
            AuditActions.PlanRequestRejectedByAdmin,
            performedByUserId,
            ipAddress,
            planRequest.UserId,
            reason,
            new { requestId, status = nameof(PlanRequestStatus.Pending) },
            new { requestId, status = planRequest.Status.ToString() },
            ct);

        _logger.LogInformation(
            "Plan request rejected. RequestId={RequestId}, UserId={UserId}, RejectedBy={RejectedBy}",
            requestId, planRequest.UserId, performedByUserId);

        var rejectedPlan = await _plans.GetByIdAsync(planRequest.PlanId, ct);
        if (rejectedPlan is not null)
        {
            await _billing.PlanRequestRejectedAsync(planRequest.UserId, requestId, rejectedPlan.Tier, reason, ct);
        }

        return AdminOperationResult.Ok("Plan request rejected.");
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
