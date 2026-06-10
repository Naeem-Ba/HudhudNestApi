using PropertyApi.Application.Auth.Models;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Auth.Interfaces;

public interface IIdentityUserService
{
    Task<User?> FindByEmailAsync(
        string email,
        CancellationToken ct = default);

    Task<User?> FindByIdAsync(
        Guid userId,
        CancellationToken ct = default);

    Task<bool> CheckPasswordAsync(
        User user,
        string password,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetRolesAsync(
        User user,
        CancellationToken ct = default);

    Task<IdentityOperationResult> CreateAsync(
        User user,
        string password,
        CancellationToken ct = default);

    Task<IdentityOperationResult> AddToRoleAsync(
        User user,
        string role,
        CancellationToken ct = default);

    Task<string> GeneratePasswordResetTokenAsync(
        User user,
        CancellationToken ct = default);

    Task<IdentityOperationResult> ResetPasswordAsync(
        User user,
        string token,
        string newPassword,
        CancellationToken ct = default);

    Task<IdentityOperationResult> UpdateSecurityStampAsync(
        User user,
        CancellationToken ct = default);

    Task<IdentityOperationResult> UpdateAsync(
        User user,
        CancellationToken ct = default);
}