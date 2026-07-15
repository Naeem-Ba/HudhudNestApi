using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Interfaces;

/// <summary>
/// Identity capabilities required by the email/password login workflow.
/// </summary>
public interface ILoginIdentityService
{
    Task<IdentityAccountSnapshot?> FindByEmailAsync(
        string email,
        CancellationToken ct = default);

    Task<bool> CheckPasswordAsync(
        Guid identityId,
        string password,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetRolesAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityOperationResult> RecordSuccessfulLoginAsync(
        Guid identityId,
        DateTime loginAtUtc,
        CancellationToken ct = default);
}
