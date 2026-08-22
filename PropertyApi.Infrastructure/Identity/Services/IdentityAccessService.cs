using Microsoft.AspNetCore.Identity;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Infrastructure.Identity.Entities;

namespace PropertyApi.Infrastructure.Identity.Services;

/// <summary>
/// Access and authentication-related operations for user accounts, including
/// password verification with integrated account lockout (AccessFailedCount tracking).
/// </summary>
public sealed class IdentityAccessService
{
    private readonly UserManager<ApplicationUser> _users;
    public IdentityAccessService(UserManager<ApplicationUser> users) => _users = users;

    /// <summary>
    /// A throwaway user carrying a real password hash, used only to spend the same
    /// hashing time on the "email not found" path that a real verification spends.
    /// The hash is produced once from a value no one can log in with, since this user
    /// is never persisted and never matched against a request.
    /// </summary>
    private static readonly Lazy<(ApplicationUser User, string Hash)> DummyCredential =
        new(() =>
        {
            var user = new ApplicationUser { Id = Guid.Empty, UserName = "dummy" };
            var hash = new PasswordHasher<ApplicationUser>()
                .HashPassword(user, "not-a-real-password-" + Guid.NewGuid().ToString("N"));

            return (user, hash);
        });

    /// <summary>
    /// Burns one password-hash verification and discards the result, so that a login
    /// attempt for an address that does not exist takes about as long as one for an
    /// address that does. See ILoginIdentityService.VerifyDummyPasswordAsync.
    /// </summary>
    public Task VerifyDummyPasswordAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var (user, hash) = DummyCredential.Value;

        // The result is intentionally unused: the work is the point, not the answer.
        _ = _users.PasswordHasher.VerifyHashedPassword(user, hash, "attempt");

        return Task.CompletedTask;
    }

    public async Task<IdentityOperationResult> AddLoginAsync(
        Guid id, string provider, string key, string displayName, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var user = await IdentityAdapterMapping.RequireAsync(_users, id, ct);
        return IdentityAdapterMapping.Result(await _users.AddLoginAsync(
            user,
            new UserLoginInfo(provider, key, displayName)));
    }

    /// <summary>
    /// Verifies the password and manages the account lockout mechanism.
    /// Increments AccessFailedCount on failure; resets it on success.
    /// Returns failure if the account is currently locked.
    /// </summary>
    public async Task<LoginPasswordVerificationResult> VerifyPasswordWithLockoutAsync(
        Guid id,
        string password,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var user = await IdentityAdapterMapping.RequireAsync(_users, id, ct);

        // Check if account is already locked
        if (await _users.IsLockedOutAsync(user))
        {
            return LoginPasswordVerificationResult.LockedOut;
        }

        var isPasswordValid = await _users.CheckPasswordAsync(user, password);

        if (isPasswordValid)
        {
            // Reset failed attempts on successful password verification
            if (user.AccessFailedCount > 0)
            {
                await _users.ResetAccessFailedCountAsync(user);
            }
            return LoginPasswordVerificationResult.Success;
        }

        // Increment failed attempt counter
        var result = await _users.AccessFailedAsync(user);
        if (!result.Succeeded)
        {
            // Log but don't fail the password check; the count update failed but we should still reject
            return LoginPasswordVerificationResult.InvalidPassword;
        }

        return LoginPasswordVerificationResult.InvalidPassword;
    }

    /// <summary>
    /// Legacy method for backward compatibility. Directly checks password without lockout tracking.
    /// DO NOT USE IN NEW CODE - use VerifyPasswordWithLockoutAsync instead.
    /// </summary>
    [Obsolete("Use VerifyPasswordWithLockoutAsync instead to include account lockout protection.")]
    public async Task<bool> CheckPasswordAsync(Guid id, string password, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var user = await IdentityAdapterMapping.RequireAsync(_users, id, ct);
        return await _users.CheckPasswordAsync(user, password);
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(Guid id, CancellationToken ct)
    {
        var user = await IdentityAdapterMapping.RequireAsync(_users, id, ct);
        return (await _users.GetRolesAsync(user)).ToArray();
    }

    public async Task<IdentityOperationResult> AddToRoleAsync(Guid id, string role, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        var user = await IdentityAdapterMapping.RequireAsync(_users, id, ct);
        return IdentityAdapterMapping.Result(await _users.AddToRoleAsync(user, role));
    }

    /// <summary>
    /// Checks if the account is currently locked due to too many failed login attempts.
    /// </summary>
    public async Task<bool> IsLockedOutAsync(Guid id, CancellationToken ct)
    {
        var user = await IdentityAdapterMapping.RequireAsync(_users, id, ct);
        return await _users.IsLockedOutAsync(user);
    }

    /// <summary>
    /// Gets the number of failed access attempts for the account.
    /// </summary>
    public async Task<int> GetAccessFailedCountAsync(Guid id, CancellationToken ct)
    {
        var user = await IdentityAdapterMapping.RequireAsync(_users, id, ct);
        return user.AccessFailedCount;
    }

    /// <summary>
    /// Gets the lockout end time for the account (if locked).
    /// </summary>
    public async Task<DateTimeOffset?> GetLockoutEndAsync(Guid id, CancellationToken ct)
    {
        var user = await IdentityAdapterMapping.RequireAsync(_users, id, ct);
        return user.LockoutEnd;
    }

    public async Task<IdentityOperationResult> RecordSuccessfulLoginAsync(
        Guid id,
        DateTime loginAtUtc,
        CancellationToken ct)
    {
        var user = await IdentityAdapterMapping.RequireAsync(_users, id, ct);
        user.LastLoginAt = loginAtUtc;
        user.UpdatedAt = loginAtUtc;
        return IdentityAdapterMapping.Result(await _users.UpdateAsync(user));
    }
}
