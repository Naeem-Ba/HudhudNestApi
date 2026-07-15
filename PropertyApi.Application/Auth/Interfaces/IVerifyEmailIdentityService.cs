using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Interfaces;

/// <summary>
/// Identity capabilities required by the verify-email workflow.
/// </summary>
public interface IVerifyEmailIdentityService
{
    Task<IdentityAccountSnapshot?> FindByIdAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityOperationResult> ConfirmEmailAsync(
        Guid identityId,
        string token,
        CancellationToken ct = default);
}
