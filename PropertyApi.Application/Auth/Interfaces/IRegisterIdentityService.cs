using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Interfaces;

/// <summary>
/// Identity capabilities required by the email registration workflow.
/// </summary>
public interface IRegisterIdentityService
{
    Task<IdentityAccountSnapshot?> FindByEmailAsync(
        string email,
        CancellationToken ct = default);

    Task<IdentityOperationResult> CreateAsync(
        CreateIdentityAccount request,
        CancellationToken ct = default);

    Task<IdentityOperationResult> AddToRoleAsync(
        Guid identityId,
        string role,
        CancellationToken ct = default);
}
