using HudhudNestApi.Application.Auth.Models;

namespace HudhudNestApi.Application.Auth.Interfaces;

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

    /// <summary>
    /// Runs one password hash verification against a fixed dummy hash and discards the
    /// result.
    ///
    /// Login used to return as soon as the email was not found, without hashing
    /// anything. Password hashing is deliberately expensive, so "no such account"
    /// answered measurably faster than "wrong password" -- a timing oracle that let an
    /// attacker enumerate registered addresses without ever seeing a different status
    /// code. Calling this on the not-found path makes both paths pay the same cost.
    /// </summary>
    Task VerifyDummyPasswordAsync(CancellationToken ct = default);
}
