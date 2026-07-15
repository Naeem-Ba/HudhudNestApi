using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Interfaces;

/// <summary>
/// Identity capabilities required by the reset-password workflow.
/// </summary>
public interface IResetPasswordIdentityService
{
    Task<IdentityAccountSnapshot?> FindByEmailAsync(
        string email,
        CancellationToken ct = default);

    Task<bool> CheckPasswordAsync(
        Guid identityId,
        string password,
        CancellationToken ct = default);

    Task<IdentityOperationResult> ResetPasswordAsync(
        Guid identityId,
        string token,
        string newPassword,
        CancellationToken ct = default);

    Task<IdentityOperationResult> UpdateSecurityStampAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityOperationResult> RecordCredentialChangeAsync(
        Guid identityId,
        DateTime changedAtUtc,
        CancellationToken ct = default);
}
