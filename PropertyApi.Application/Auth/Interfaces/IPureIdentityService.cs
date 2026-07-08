using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Interfaces;

/// <summary>
/// Framework-neutral Identity boundary implemented by Infrastructure.
///
/// Application handlers use Guid identifiers and neutral snapshots
/// instead of ASP.NET Core Identity user entities.
/// </summary>
public interface IPureIdentityService
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
    Task<IdentityAccountSnapshot?> FindByPhoneNumberAsync(
    string phoneNumber,
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

    Task<bool> CheckPasswordAsync(
        Guid identityId,
        string password,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetRolesAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityOperationResult> AddToRoleAsync(
        Guid identityId,
        string role,
        CancellationToken ct = default);

    Task<IdentityOperationResult> ConfirmPhoneNumberAsync(
    Guid identityId,
    DateTime confirmedAtUtc,
    CancellationToken ct = default);

    Task<IdentityOperationResult> UpdateSecurityStampAsync(
        Guid identityId,
        CancellationToken ct = default);
    Task<IdentityOperationResult> SoftDeleteAsync(
    Guid identityId,
    DateTime deletedAtUtc,
    CancellationToken ct = default);
    Task<IdentityOperationResult> RecordSuccessfulLoginAsync(
        Guid identityId,
        DateTime loginAtUtc,
        CancellationToken ct = default);

    Task<string> GeneratePasswordResetTokenAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityOperationResult> ResetPasswordAsync(
        Guid identityId,
        string token,
        string newPassword,
        CancellationToken ct = default);

    Task<IdentityOperationResult> ChangePasswordAsync(
        Guid identityId,
        string currentPassword,
        string newPassword,
        CancellationToken ct = default);

    Task<IdentityOperationResult> RecordCredentialChangeAsync(
        Guid identityId,
        DateTime changedAtUtc,
        CancellationToken ct = default);

    Task<IdentityOperationResult> SetEmailAsync(
        Guid identityId,
        string email,
        CancellationToken ct = default);

    Task<string> GenerateEmailConfirmationTokenAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityOperationResult> ConfirmEmailAsync(
        Guid identityId,
        string token,
        CancellationToken ct = default);
    Task<IdentityOperationResult> SetPhoneNumberAsync(
    Guid identityId,
    string? phoneNumber,
    DateTime changedAtUtc,
    CancellationToken ct = default);
}