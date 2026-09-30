using HudhudNestApi.Application.Auth.Models;

namespace HudhudNestApi.Application.Auth.Interfaces;

/// <summary>
/// Identity capabilities required by the social-login workflow.
/// This is one of the Phase B slices away from a broad Identity boundary.
/// </summary>
public interface ISocialLoginIdentityService
{
    Task<IdentityAccountSnapshot?> FindByIdAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityAccountSnapshot?> FindByEmailAsync(
        string email,
        CancellationToken ct = default);

    Task<IdentityAccountSnapshot?> FindByLoginAsync(
        string loginProvider,
        string providerKey,
        CancellationToken ct = default);

    Task<IdentityOperationResult> CreateAsync(
        CreateIdentityAccount request,
        CancellationToken ct = default);

    Task<IdentityOperationResult> AddLoginAsync(
        Guid identityId,
        string loginProvider,
        string providerKey,
        string providerDisplayName,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetRolesAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityOperationResult> AddToRoleAsync(
        Guid identityId,
        string role,
        CancellationToken ct = default);

    Task<IdentityOperationResult> RecordSuccessfulLoginAsync(
        Guid identityId,
        DateTime loginAtUtc,
        CancellationToken ct = default);
}
