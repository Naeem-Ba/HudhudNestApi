using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Admin.DTOs;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Identity.Entities;

namespace HudhudNestApi.Infrastructure.Admin;

public sealed class AdminIdentityService : IAdminIdentityService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    private readonly IAuditLogService _auditLogs;
    private readonly ILogger<AdminIdentityService> _logger;
    private readonly IUserSecurityStampCacheInvalidator _stampCache;

    public AdminIdentityService(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        IAuditLogService auditLogs,
        ILogger<AdminIdentityService> logger,
        IUserSecurityStampCacheInvalidator stampCache)
    {
        _userManager = userManager;
        _db = db;
        _auditLogs = auditLogs;
        _logger = logger;
        _stampCache = stampCache;
    }

    public async Task<AdminOperationResult> SetSingleRoleAsync(
        Guid userId,
        string role,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var user = await FindActiveUserAsync(userId);
        if (user is null)
            return AdminOperationResult.UserNotFound();

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var currentRoles = await _userManager.GetRolesAsync(user);

        if (currentRoles.Count > 0)
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                return MapFailure("Could not remove the old roles.", removeResult);
            }
        }

        var addResult = await _userManager.AddToRoleAsync(user, role);
        if (!addResult.Succeeded)
        {
            await transaction.RollbackAsync(ct);
            return MapFailure("Could not add the new role.", addResult);
        }

        var updateResult = await TouchUserAfterRoleChangeAsync(user);
        if (!updateResult.Succeeded)
        {
            await transaction.RollbackAsync(ct);
            return updateResult;
        }

        await LogRoleChangeAsync(
            performedByUserId,
            ipAddress,
            targetUserId: userId,
            oldRoles: currentRoles,
            newRoles: new[] { role },
            ct: ct);

        await transaction.CommitAsync(ct);

        return AdminOperationResult.Ok($"Role updated to '{role}'.");
    }

    public async Task<AdminOperationResult> AssignRoleAsync(
        Guid userId,
        string role,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var user = await FindActiveUserAsync(userId);
        if (user is null)
            return AdminOperationResult.UserNotFound();

        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.Any(currentRole => string.Equals(currentRole, role, StringComparison.OrdinalIgnoreCase)))
            return AdminOperationResult.Ok($"Role '{role}' already assigned.");

        var addResult = await _userManager.AddToRoleAsync(user, role);
        if (!addResult.Succeeded)
            return MapFailure("Could not add the role.", addResult);

        var updateResult = await TouchUserAfterRoleChangeAsync(user);
        if (!updateResult.Succeeded)
            return updateResult;

        var newRoles = currentRoles
            .Append(role)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await LogRoleChangeAsync(
            performedByUserId,
            ipAddress,
            targetUserId: userId,
            oldRoles: currentRoles,
            newRoles: newRoles,
            ct: ct);

        return AdminOperationResult.Ok($"Role '{role}' assigned.");
    }

    public async Task<AdminOperationResult> RemoveRoleAsync(
        Guid userId,
        string role,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var user = await FindActiveUserAsync(userId);
        if (user is null)
            return AdminOperationResult.UserNotFound();

        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.All(currentRole => !string.Equals(currentRole, role, StringComparison.OrdinalIgnoreCase)))
            return AdminOperationResult.Ok($"Role '{role}' was not assigned.");

        var removeResult = await _userManager.RemoveFromRoleAsync(user, role);
        if (!removeResult.Succeeded)
            return MapFailure("Could not remove the role.", removeResult);

        var updateResult = await TouchUserAfterRoleChangeAsync(user);
        if (!updateResult.Succeeded)
            return updateResult;

        var newRoles = currentRoles
            .Where(currentRole => !string.Equals(currentRole, role, StringComparison.OrdinalIgnoreCase))
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await LogRoleChangeAsync(
            performedByUserId,
            ipAddress,
            targetUserId: userId,
            oldRoles: currentRoles,
            newRoles: newRoles,
            ct: ct);

        return AdminOperationResult.Ok($"Role '{role}' removed.");
    }

    public async Task<AdminOperationResult> DisableUserAsync(
        Guid userId,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        var user = await FindActiveUserAsync(userId);
        if (user is null)
            return AdminOperationResult.UserNotFound();

        // Security audit 2026-10-03, F-10: an admin could lock themselves out. The caller is by
        // definition an active admin, so refusing self-disable also guarantees an admin always remains.
        if (userId == performedByUserId)
            return AdminOperationResult.BadRequest("You cannot disable your own account.");

        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = DateTime.UtcNow;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return MapFailure("Could not disable the user.", updateResult);

        // The JWT pipeline trusts a 5-minute cached stamp snapshot; without this the disabled
        // account keeps working until it expires (security audit 2026-10-03, F-03).
        await _stampCache.InvalidateAsync(user.Id, ct);

        await _auditLogs.LogAsync(
            userId: performedByUserId,
            action: AuditActions.UserDisabled,
            ipAddress: ipAddress,
            oldValue: null,
            newValue: JsonSerializer.Serialize(new { targetUserId = user.Id, timestamp = DateTime.UtcNow }),
            ct: ct);

        return AdminOperationResult.Ok("User disabled.");
    }

    private Task<ApplicationUser?> FindActiveUserAsync(Guid userId)
        => _userManager.FindByIdAsync(userId.ToString());

    private async Task<AdminOperationResult> TouchUserAfterRoleChangeAsync(ApplicationUser user)
    {
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = DateTime.UtcNow;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return MapFailure("Could not update the user.", updateResult);

        // Same reason as DisableUserAsync: a demoted admin must not keep the old roles claim alive
        // behind the stamp cache.
        await _stampCache.InvalidateAsync(user.Id);

        return AdminOperationResult.Ok("User updated.");
    }

    private Task LogRoleChangeAsync(
        Guid performedByUserId,
        string? ipAddress,
        Guid targetUserId,
        IEnumerable<string> oldRoles,
        IEnumerable<string> newRoles,
        CancellationToken ct)
    {
        static string[] Normalize(IEnumerable<string> roles)
            => roles
                .OrderBy(role => role, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        return _auditLogs.LogAsync(
            userId: performedByUserId,
            action: AuditActions.RoleChanged,
            ipAddress: ipAddress,
            oldValue: JsonSerializer.Serialize(new
            {
                targetUserId,
                roles = Normalize(oldRoles)
            }),
            newValue: JsonSerializer.Serialize(new
            {
                targetUserId,
                roles = Normalize(newRoles),
                timestamp = DateTime.UtcNow
            }),
            ct: ct);
    }

    private AdminOperationResult MapFailure(string message, IdentityResult result)
    {
        var errors = result.Errors.Select(error => error.Description).ToArray();

        _logger.LogWarning(
            "Admin identity operation failed. Message={Message}. Errors={Errors}",
            message,
            string.Join(", ", errors));

        return AdminOperationResult.BadRequest(message, errors);
    }
}
