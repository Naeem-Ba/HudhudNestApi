using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Admin.DTOs;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Admin;

public sealed class AdminIdentityService : IAdminIdentityService
{
    private readonly UserManager<User> _userManager;
    private readonly AppDbContext _db;
    private readonly ILogger<AdminIdentityService> _logger;

    public AdminIdentityService(
        UserManager<User> userManager,
        AppDbContext db,
        ILogger<AdminIdentityService> logger)
    {
        _userManager = userManager;
        _db = db;
        _logger = logger;
    }

    public async Task<AdminOperationResult> SetSingleRoleAsync(
        Guid userId,
        string role,
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

        var updateResult = await UpdateUserRoleFlagsAsync(user, role, ct);
        if (!updateResult.Succeeded)
        {
            await transaction.RollbackAsync(ct);
            return updateResult;
        }

        await transaction.CommitAsync(ct);

        return AdminOperationResult.Ok($"Role updated to '{role}'.");
    }

    public async Task<AdminOperationResult> AssignRoleAsync(
        Guid userId,
        string role,
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

        var updateResult = await UpdateUserRoleFlagsAsync(user, role, ct);
        if (!updateResult.Succeeded)
            return updateResult;

        return AdminOperationResult.Ok($"Role '{role}' assigned.");
    }

    public async Task<AdminOperationResult> RemoveRoleAsync(
        Guid userId,
        string role,
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

        var remainingRoles = await _userManager.GetRolesAsync(user);
        user.IsAgent = remainingRoles.Any(currentRole =>
            string.Equals(currentRole, RoleNames.Agent, StringComparison.OrdinalIgnoreCase));

        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = DateTime.UtcNow;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return MapFailure("Could not update the user.", updateResult);

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

    private Task<User?> FindActiveUserAsync(Guid userId)
        => _userManager.FindByIdAsync(userId.ToString());

    private async Task<AdminOperationResult> UpdateUserRoleFlagsAsync(
        User user,
        string effectiveRole,
        CancellationToken ct)
    {
        user.IsAgent = string.Equals(effectiveRole, RoleNames.Agent, StringComparison.OrdinalIgnoreCase);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = DateTime.UtcNow;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
            return MapFailure("Could not update the user.", updateResult);

        return AdminOperationResult.Ok("User updated.");
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
