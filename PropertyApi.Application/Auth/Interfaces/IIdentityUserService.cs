using PropertyApi.Application.Auth.Models;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Auth.Interfaces;

/// <summary>
/// Application-level abstraction over ASP.NET Identity.
/// Handlers depend on this interface instead of ASP.NET Core Identity services.
/// Infrastructure provides the implementation.
/// </summary>
public interface IIdentityUserService
{
    Task<User?> FindByEmailAsync(string email, CancellationToken ct = default);

    Task<User?> FindByIdAsync(Guid userId, CancellationToken ct = default);

    Task<User?> FindByUserNameAsync(string userName, CancellationToken ct = default);

    Task<User?> FindByLoginAsync(
        string loginProvider,
        string providerKey,
        CancellationToken ct = default);

    Task<IdentityOperationResult> AddLoginAsync(
        User user,
        string loginProvider,
        string providerKey,
        string providerDisplayName,
        CancellationToken ct = default);

    Task<bool> CheckPasswordAsync(User user, string password, CancellationToken ct = default);

    Task<IdentityOperationResult> ChangePasswordAsync(
        User user,
        string currentPassword,
        string newPassword,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetRolesAsync(User user, CancellationToken ct = default);

    Task<IdentityOperationResult> CreateAsync(User user, string password, CancellationToken ct = default);

    /// <summary>
    /// Creates a user without password. Used by phone-only registration.
    /// </summary>
    Task<IdentityOperationResult> CreateAsync(User user, CancellationToken ct = default);

    Task<IdentityOperationResult> AddToRoleAsync(User user, string role, CancellationToken ct = default);

    Task<IdentityOperationResult> UpdateAsync(User user, CancellationToken ct = default);

    Task<IdentityOperationResult> UpdateSecurityStampAsync(User user, CancellationToken ct = default);

    Task<IdentityOperationResult> ResetPasswordAsync(
        User user,
        string token,
        string newPassword,
        CancellationToken ct = default);

    Task<string> GeneratePasswordResetTokenAsync(User user, CancellationToken ct = default);

    Task<IdentityOperationResult> SetEmailAsync(
        User user,
        string email,
        CancellationToken ct = default);

    Task<string> GenerateEmailConfirmationTokenAsync(
        User user,
        CancellationToken ct = default);

    Task<IdentityOperationResult> ConfirmEmailAsync(
        User user,
        string token,
        CancellationToken ct = default);
}


