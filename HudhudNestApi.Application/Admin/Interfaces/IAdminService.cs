using HudhudNestApi.Application.Admin.DTOs;
using HudhudNestApi.Application.Properties.DTOs;

namespace HudhudNestApi.Application.Admin.Interfaces;

public interface IAdminService
{
    Task<PagedResult<AdminUserDto>> GetUsersWithPaginationAsync(
        int page,
        int pageSize,
        string? role,
        string? search = null,
        string? planTier = null,
        string? accountStatus = null,
        CancellationToken ct = default);

    Task<AdminUserDetailDto?> GetUserDetailAsync(
        Guid userId,
        CancellationToken ct = default);

    IReadOnlyList<string> GetRoles();

    Task<AdminOperationResult> SetUserRoleAsync(
        Guid userId,
        string role,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default);

    Task<AdminOperationResult> AssignRoleAsync(
        Guid userId,
        string role,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default);

    Task<AdminOperationResult> RemoveRoleAsync(
        Guid userId,
        string role,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default);

    Task<AdminOperationResult> DisableUserAsync(
        Guid userId,
        Guid performedByUserId,
        string? ipAddress,
        CancellationToken ct = default);
}
