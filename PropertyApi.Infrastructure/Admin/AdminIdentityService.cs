using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Admin.DTOs;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Audit.Constants;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Infrastructure.Identity.Entities;

namespace PropertyApi.Infrastructure.Admin;

public sealed class AdminIdentityService : IAdminIdentityService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    private readonly IAuditLogService _auditLogs;
    private readonly ILogger<AdminIdentityService> _logger;

    public AdminIdentityService(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        IAuditLogService auditLogs,
        ILogger<AdminIdentityService> logger)
    {
        _userManager = userManager;
        _db = db;
        _auditLogs = auditLogs;
        _logger = logger;
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
        CancellationToken ct = default)
    {
        var user = await FindActiveUserAsync(userId);
        if (user is null)
            return AdminOperationResult.UserNotFound();

        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = DateTime.UtcNow;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return MapFailure("Could not disable the user.", updateResult);

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
