using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Interfaces;

/// <summary>Framework-neutral boundary implemented by Infrastructure.</summary>
public interface IPureIdentityService
{
    Task<IdentityAccountSnapshot?> FindByIdAsync(Guid identityId, CancellationToken ct = default);
    Task<IdentityAccountSnapshot?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<IdentityOperationResult> CreateAsync(CreateIdentityAccount request, CancellationToken ct = default);
    Task<bool> CheckPasswordAsync(Guid identityId, string password, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetRolesAsync(Guid identityId, CancellationToken ct = default);
    Task<IdentityOperationResult> AddToRoleAsync(Guid identityId, string role, CancellationToken ct = default);
}
