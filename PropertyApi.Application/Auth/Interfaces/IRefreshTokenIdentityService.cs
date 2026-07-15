using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Interfaces;

/// <summary>
/// Identity capabilities required by the refresh-token workflow.
/// </summary>
public interface IRefreshTokenIdentityService
{
    Task<IdentityAccountSnapshot?> FindByIdAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetRolesAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityOperationResult> UpdateSecurityStampAsync(
        Guid identityId,
        CancellationToken ct = default);
}
