using PropertyApi.Application.Admin.DTOs;
using PropertyApi.Application.Admin.Interfaces;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Application.Admin.Services;

public sealed class AdminService : IAdminService
{
    private readonly IAdminUserQueryRepository _users;
    private readonly IAdminIdentityService _identity;

    public AdminService(
        IAdminUserQueryRepository users,
        IAdminIdentityService identity)
    {
        _users = users;
        _identity = identity;
    }

    public Task<PagedResult<AdminUserDto>> GetUsersWithPaginationAsync(
        int page,
        int pageSize,
        string? role,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var normalizedRole = NormalizeRoleOrNull(role);

        return _users.GetUsersWithPaginationAsync(
            page,
            pageSize,
            normalizedRole,
            ct);
    }

    public IReadOnlyList<string> GetRoles()
        => RoleNames.All;

    public Task<AdminOperationResult> SetUserRoleAsync(
        Guid userId,
        string role,
        CancellationToken ct = default)
    {
        var normalizedRole = NormalizeRole(role);
        if (normalizedRole is null)
            return Task.FromResult(AdminOperationResult.InvalidRole(RoleNames.All));

        return _identity.SetSingleRoleAsync(userId, normalizedRole, ct);
    }

    public Task<AdminOperationResult> AssignRoleAsync(
        Guid userId,
        string role,
        CancellationToken ct = default)
    {
        var normalizedRole = NormalizeRole(role);
        if (normalizedRole is null)
            return Task.FromResult(AdminOperationResult.InvalidRole(RoleNames.All));

        return _identity.AssignRoleAsync(userId, normalizedRole, ct);
    }

    public Task<AdminOperationResult> RemoveRoleAsync(
        Guid userId,
        string role,
        CancellationToken ct = default)
    {
        var normalizedRole = NormalizeRole(role);
        if (normalizedRole is null)
            return Task.FromResult(AdminOperationResult.InvalidRole(RoleNames.All));

        return _identity.RemoveRoleAsync(userId, normalizedRole, ct);
    }

    public Task<AdminOperationResult> DisableUserAsync(
        Guid userId,
        CancellationToken ct = default)
        => _identity.DisableUserAsync(userId, ct);

    private static string? NormalizeRoleOrNull(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;

        return NormalizeRole(role);
    }

    private static string? NormalizeRole(string? role)
        => RoleNames.All.FirstOrDefault(allowedRole =>
            string.Equals(
                allowedRole,
                role,
                StringComparison.OrdinalIgnoreCase));
}

