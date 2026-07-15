using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Interfaces;

/// <summary>
/// Identity capabilities required by user profile read workflows.
/// </summary>
public interface IUserIdentityReadService
{
    Task<IdentityAccountSnapshot?> FindByIdAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetRolesAsync(
        Guid identityId,
        CancellationToken ct = default);
}
