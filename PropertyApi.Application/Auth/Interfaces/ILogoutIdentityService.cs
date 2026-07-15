using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Interfaces;

/// <summary>
/// Identity capabilities required by the logout workflow.
/// </summary>
public interface ILogoutIdentityService
{
    Task<IdentityAccountSnapshot?> FindByIdAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityOperationResult> UpdateSecurityStampAsync(
        Guid identityId,
        CancellationToken ct = default);
}
