using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Interfaces;

/// <summary>
/// Identity capabilities required by the update-user workflow.
/// </summary>
public interface IUpdateUserIdentityService
{
    Task<IdentityAccountSnapshot?> FindByIdAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetRolesAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityOperationResult> SetPhoneNumberAsync(
        Guid identityId,
        string? phoneNumber,
        DateTime changedAtUtc,
        CancellationToken ct = default);
}
