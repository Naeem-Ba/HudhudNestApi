using PropertyApi.Application.Admin.DTOs;
using PropertyApi.Application.Properties.DTOs;

namespace PropertyApi.Application.Admin.Interfaces;

public interface IAdminService
{
    Task<PagedResult<AdminUserDto>> GetUsersWithPaginationAsync(
        int page,
        int pageSize,
        string? role,
        CancellationToken ct = default);

    IReadOnlyList<string> GetRoles();

    Task<AdminOperationResult> SetUserRoleAsync(
        Guid userId,
        string role,
        CancellationToken ct = default);

    Task<AdminOperationResult> AssignRoleAsync(
        Guid userId,
        string role,
        CancellationToken ct = default);

    Task<AdminOperationResult> RemoveRoleAsync(
        Guid userId,
        string role,
        CancellationToken ct = default);

    Task<AdminOperationResult> DisableUserAsync(
        Guid userId,
        CancellationToken ct = default);
}
