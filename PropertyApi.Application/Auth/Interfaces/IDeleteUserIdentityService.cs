using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Interfaces;

/// <summary>
/// Identity capabilities required by the delete-user workflow.
/// </summary>
public interface IDeleteUserIdentityService
{
    Task<IdentityAccountSnapshot?> FindByIdAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityOperationResult> UpdateSecurityStampAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityOperationResult> SoftDeleteAsync(
        Guid identityId,
        DateTime deletedAtUtc,
        CancellationToken ct = default);
}
