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

    /// <summary>
    /// Legacy method for backward compatibility. Directly checks password without lockout tracking.
    /// DO NOT USE IN NEW CODE - use VerifyPasswordWithLockoutAsync instead.
    /// </summary>
    [Obsolete("Use VerifyPasswordWithLockoutAsync instead to include account lockout protection.")]
    Task<bool> CheckPasswordAsync(
        Guid identityId,
        string password,
        CancellationToken ct = default);

    /// <summary>
    /// Verifies password with integrated account lockout protection.
    /// Increments AccessFailedCount on failure; resets on success.
    /// </summary>
    Task<LoginPasswordVerificationResult> VerifyPasswordWithLockoutAsync(
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

    /// <summary>
    /// Checks if the account is currently locked due to too many failed login attempts.
    /// </summary>
    Task<bool> IsLockedOutAsync(
        Guid identityId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the number of failed access attempts for the account.
    /// </summary>
    Task<int> GetAccessFailedCountAsync(
        Guid identityId,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the lockout end time for the account (if locked).
    /// </summary>
    Task<DateTimeOffset?> GetLockoutEndAsync(
        Guid identityId,
        CancellationToken ct = default);
}
