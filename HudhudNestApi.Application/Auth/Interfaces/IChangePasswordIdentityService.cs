using HudhudNestApi.Application.Auth.Models;

namespace HudhudNestApi.Application.Auth.Interfaces;

/// <summary>
/// Identity capabilities required by the change-password workflow.
/// </summary>
public interface IChangePasswordIdentityService
{
    Task<IdentityAccountSnapshot?> FindByIdAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityOperationResult> ChangePasswordAsync(
        Guid identityId,
        string currentPassword,
        string newPassword,
        CancellationToken ct = default);

    Task<IdentityOperationResult> RecordCredentialChangeAsync(
        Guid identityId,
        DateTime changedAtUtc,
        CancellationToken ct = default);

    Task<IdentityOperationResult> UpdateSecurityStampAsync(
        Guid identityId,
        CancellationToken ct = default);
}
