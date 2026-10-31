using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Admin.DTOs;
using HudhudNestApi.Application.Admin.Interfaces;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Users.Constants;
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

    /// <summary>Serialises every change that can lower the number of active administrators (phase 14, D01).</summary>
    private const long AdminRoleLockKey = 0x4C41_5354_4144_4D31;

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

        var losesAdmin = HasRole(currentRoles, RoleNames.Admin)
            && !string.Equals(role, RoleNames.Admin, StringComparison.OrdinalIgnoreCase);
        if (losesAdmin)
        {
            var guard = await EnsureAdminMayLoseAdminAsync(userId, performedByUserId, ct);
            if (guard is not null)
            {
                await transaction.RollbackAsync(ct);
                return guard;
            }
        }

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

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var currentRoles = await _userManager.GetRolesAsync(user);
        if (!HasRole(currentRoles, role))
        {
            await transaction.RollbackAsync(ct);
            return AdminOperationResult.Ok($"Role '{role}' was not assigned.");
        }

        if (string.Equals(role, RoleNames.Admin, StringComparison.OrdinalIgnoreCase))
        {
            var guard = await EnsureAdminMayLoseAdminAsync(userId, performedByUserId, ct);
            if (guard is not null)
            {
                await transaction.RollbackAsync(ct);
                return guard;
            }
        }

        var removeResult = await _userManager.RemoveFromRoleAsync(user, role);
        if (!removeResult.Succeeded)
        {
            await transaction.RollbackAsync(ct);
            return MapFailure("Could not remove the role.", removeResult);
        }

        var newRoles = currentRoles
            .Where(currentRole => !string.Equals(currentRole, role, StringComparison.OrdinalIgnoreCase))
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Phase 14, D10: an account never ends up with no role at all; "User" is the base role.
        var fellBackToUser = false;
        if (newRoles.Count == 0)
        {
            var addBase = await _userManager.AddToRoleAsync(user, RoleNames.User);
            if (!addBase.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                return MapFailure("Could not keep the base role.", addBase);
            }

            newRoles.Add(RoleNames.User);
            fellBackToUser = true;
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
            newRoles: newRoles,
            ct: ct);

        await transaction.CommitAsync(ct);

        return AdminOperationResult.Ok(fellBackToUser
            ? $"Role '{role}' removed; the user keeps the base 'User' role."
            : $"Role '{role}' removed.");
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

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        // Phase 14, D01: two admins disabling each other in parallel must not leave none.
        var roles = await _userManager.GetRolesAsync(user);
        if (HasRole(roles, RoleNames.Admin))
        {
            var guard = await EnsureAdminMayLoseAdminAsync(userId, performedByUserId, ct);
            if (guard is not null)
            {
                await transaction.RollbackAsync(ct);
                return guard;
            }
        }

        var now = DateTime.UtcNow;
        user.IsDeleted = true;
        user.DeletedAt = now;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = now;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            await transaction.RollbackAsync(ct);
            return MapFailure("Could not disable the user.", updateResult);
        }

        // Phase 14, D02: a disabled owner's listings leave the site (reversible take-down that
        // EnableUserAsync lifts again). Only published listings are touched, so a draft stays a draft.
        var hidden = await HideListingsOfDisabledUserAsync(userId, performedByUserId, ct);

        await _auditLogs.LogAsync(
            userId: performedByUserId,
            action: AuditActions.UserDisabled,
            ipAddress: ipAddress,
            oldValue: null,
            newValue: JsonSerializer.Serialize(new
            {
                targetUserId = user.Id,
                hiddenListings = hidden.Properties,
                hiddenShortStayListings = hidden.ShortStay,
                timestamp = now
            }),
            ct: ct);

        await transaction.CommitAsync(ct);

        // The JWT pipeline trusts a 5-minute cached stamp snapshot; without this the disabled
        // account keeps working until it expires (security audit 2026-10-03, F-03).
        await _stampCache.InvalidateAsync(user.Id, ct);

        return AdminOperationResult.Ok("User disabled.");
    }

    public async Task<AdminOperationResult> EnableUserAsync(
        Guid userId,
        string reason,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default)
    {
        // The Identity user set hides disabled rows behind a query filter; this is the one place that
        // must see them.
        var user = await _db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return AdminOperationResult.UserNotFound();

        if (!user.IsDeleted)
            return AdminOperationResult.ConflictResult("The user is not disabled.");

        // An account the owner deleted (anonymised, irreversible) must never come back through here.
        var profile = await _db.UserAccounts.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == userId, ct);
        if (profile is { IsDeleted: true })
            return AdminOperationResult.ConflictResult("This account was deleted by its owner and cannot be re-enabled.");

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        user.IsDeleted = false;
        user.DeletedAt = null;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = DateTime.UtcNow;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            await transaction.RollbackAsync(ct);
            return MapFailure("Could not re-enable the user.", updateResult);
        }

        var restored = await RestoreListingsOfEnabledUserAsync(userId, ct);

        await _auditLogs.LogAsync(
            userId: performedByUserId,
            action: AuditActions.UserEnabled,
            ipAddress: ipAddress,
            oldValue: null,
            newValue: JsonSerializer.Serialize(new
            {
                targetUserId = user.Id,
                reason,
                restoredListings = restored.Properties,
                restoredShortStayListings = restored.ShortStay,
                timestamp = DateTime.UtcNow
            }),
            ct: ct);

        await transaction.CommitAsync(ct);
        await _stampCache.InvalidateAsync(user.Id, ct);

        return AdminOperationResult.Ok("User re-enabled.");
    }

    private async Task<(int Properties, int ShortStay)> HideListingsOfDisabledUserAsync(
        Guid ownerId, Guid administratorId, CancellationToken ct)
    {
        var properties = await _db.Properties
            .Where(p => p.OwnerId == ownerId && p.IsPublished && p.TakenDownAt == null)
            .ToListAsync(ct);
        foreach (var property in properties)
            property.TakeDown(administratorId, Property.AccountDisabledTakeDownReason);

        var shortStay = await _db.ShortStayListings
            .Where(l => l.OwnerId == ownerId && l.IsPublished && l.TakenDownAt == null)
            .ToListAsync(ct);
        foreach (var listing in shortStay)
            listing.TakeDown(administratorId, Property.AccountDisabledTakeDownReason);

        await _db.SaveChangesAsync(ct);
        return (properties.Count, shortStay.Count);
    }

    private async Task<(int Properties, int ShortStay)> RestoreListingsOfEnabledUserAsync(
        Guid ownerId, CancellationToken ct)
    {
        // Only what the disable itself took down; a listing an admin took down for another reason stays down.
        var properties = await _db.Properties
            .Where(p => p.OwnerId == ownerId && p.TakenDownReason == Property.AccountDisabledTakeDownReason)
            .ToListAsync(ct);
        foreach (var property in properties)
            property.RestoreFromTakeDown();

        var shortStay = await _db.ShortStayListings
            .Where(l => l.OwnerId == ownerId && l.TakenDownReason == Property.AccountDisabledTakeDownReason)
            .ToListAsync(ct);
        foreach (var listing in shortStay)
            listing.RestoreFromTakeDown();

        await _db.SaveChangesAsync(ct);
        return (properties.Count, shortStay.Count);
    }

    /// <summary>
    /// Phase 14, D01. Called inside the caller's transaction before an administrator loses the Admin
    /// role or is disabled. An administrator cannot remove their own Admin role (another admin must),
    /// and the number of active administrators never drops below one, even when two admins act in
    /// parallel (the advisory lock makes the second one see the first one's result).
    /// </summary>
    private async Task<AdminOperationResult?> EnsureAdminMayLoseAdminAsync(
        Guid targetUserId, Guid performedByUserId, CancellationToken ct)
    {
        if (targetUserId == performedByUserId)
        {
            return AdminOperationResult.ConflictResult(
                "You cannot remove your own Admin role. Ask another administrator to do it.");
        }

        if (_db.Database.IsNpgsql())
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({AdminRoleLockKey});", ct);
        }

        // _db.Users hides disabled accounts, so this counts only administrators who can still act.
        var activeAdmins = await (
                from userRole in _db.UserRoles.AsNoTracking()
                join identityRole in _db.Roles.AsNoTracking() on userRole.RoleId equals identityRole.Id
                join user in _db.Users.AsNoTracking() on userRole.UserId equals user.Id
                where identityRole.Name == RoleNames.Admin
                select userRole.UserId)
            .Distinct()
            .CountAsync(ct);

        return activeAdmins <= 1
            ? AdminOperationResult.ConflictResult("The platform must keep at least one active administrator.")
            : null;
    }

    private static bool HasRole(IEnumerable<string> roles, string role)
        => roles.Any(current => string.Equals(current, role, StringComparison.OrdinalIgnoreCase));

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
        // Phase 14 (D13): the loser of a race between two administrators gets a plain 409, not a 400 that
        // quotes the framework's internal wording.
        if (result.Errors.Any(error => error.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure)))
            return AdminOperationResult.ConflictResult("The user was changed by another administrator. Reload and try again.");

        var errors = result.Errors.Select(error => error.Description).ToArray();

        _logger.LogWarning(
            "Admin identity operation failed. Message={Message}. Errors={Errors}",
            message,
            string.Join(", ", errors));

        return AdminOperationResult.BadRequest(message, errors);
    }
}
