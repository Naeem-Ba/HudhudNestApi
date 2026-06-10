using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Infrastructure.Identity.Services;

/// <summary>
/// Infrastructure implementation of IIdentityUserService using ASP.NET Identity.
/// This keeps UserManager/IdentityResult out of the Application layer.
/// </summary>
public sealed class IdentityUserService : IIdentityUserService
{
    private readonly UserManager<User> _userManager;
    private readonly ILogger<IdentityUserService> _logger;

    public IdentityUserService(
        UserManager<User> userManager,
        ILogger<IdentityUserService> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public Task<User?> FindByEmailAsync(string email, CancellationToken ct = default)
        => _userManager.FindByEmailAsync(email);

    public Task<User?> FindByIdAsync(Guid userId, CancellationToken ct = default)
        => _userManager.FindByIdAsync(userId.ToString());

    public Task<bool> CheckPasswordAsync(User user, string password, CancellationToken ct = default)
        => _userManager.CheckPasswordAsync(user, password);

    public async Task<IReadOnlyList<string>> GetRolesAsync(User user, CancellationToken ct = default)
    {
        var roles = await _userManager.GetRolesAsync(user);
        return roles.ToArray();
    }

    public async Task<IdentityOperationResult> CreateAsync(
        User user,
        string password,
        CancellationToken ct = default)
    {
        var result = await _userManager.CreateAsync(user, password);
        return Map(result);
    }

    public async Task<IdentityOperationResult> AddToRoleAsync(
        User user,
        string role,
        CancellationToken ct = default)
    {
        var result = await _userManager.AddToRoleAsync(user, role);
        return Map(result);
    }

    public async Task<IdentityOperationResult> UpdateAsync(User user, CancellationToken ct = default)
    {
        var result = await _userManager.UpdateAsync(user);
        return Map(result);
    }

    public async Task<IdentityOperationResult> UpdateSecurityStampAsync(
        User user,
        CancellationToken ct = default)
    {
        var result = await _userManager.UpdateSecurityStampAsync(user);
        return Map(result);
    }

    public async Task<IdentityOperationResult> ResetPasswordAsync(
        User user,
        string token,
        string newPassword,
        CancellationToken ct = default)
    {
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);
        return Map(result);
    }

    public Task<string> GeneratePasswordResetTokenAsync(User user, CancellationToken ct = default)
        => _userManager.GeneratePasswordResetTokenAsync(user);

    private IdentityOperationResult Map(IdentityResult result)
    {
        if (result.Succeeded)
            return IdentityOperationResult.Success();

        var errors = result.Errors.Select(error => error.Description).ToArray();
        _logger.LogDebug("Identity operation failed. Errors: {Errors}", string.Join(", ", errors));
        return IdentityOperationResult.Failed(errors);
    }
}
