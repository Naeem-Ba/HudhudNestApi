using PropertyApi.Application.Admin.DTOs;

namespace PropertyApi.Application.Admin.Interfaces;

public interface IAdminIdentityService
{
    Task<AdminOperationResult> SetSingleRoleAsync(
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
        CancellationToken ct = default);
}
