using Microsoft.AspNetCore.Identity;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Infrastructure.Identity.Services;

/// <summary>
/// Framework-neutral Identity adapter used while handlers are migrated away from
/// IIdentityUserService. The legacy Domain User is deliberately contained inside
/// Infrastructure and will be replaced by ApplicationUser in the final cutover.
/// </summary>
public sealed class PureIdentityService : IPureIdentityService
{
    private readonly UserManager<User> _userManager;

    public PureIdentityService(UserManager<User> userManager)
        => _userManager = userManager;

    public async Task<IdentityAccountSnapshot?> FindByIdAsync(
        Guid identityId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var user = await _userManager.FindByIdAsync(identityId.ToString());
        return user is null ? null : Map(user);
    }

    public async Task<IdentityAccountSnapshot?> FindByEmailAsync(
        string email,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ct.ThrowIfCancellationRequested();
        var user = await _userManager.FindByEmailAsync(email.Trim());
        return user is null ? null : Map(user);
    }

    public async Task<IdentityOperationResult> CreateAsync(
        CreateIdentityAccount request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var user = new User
        {
            Id = request.UserAccountId,
            UserName = request.Email ?? request.PhoneNumber ?? request.UserAccountId.ToString("N"),
            Email = request.Email,
            PhoneNumber = request.PhoneNumber
        };

        var result = string.IsNullOrWhiteSpace(request.Password)
            ? await _userManager.CreateAsync(user)
            : await _userManager.CreateAsync(user, request.Password);

        return Map(result);
    }

    public async Task<bool> CheckPasswordAsync(
        Guid identityId,
        string password,
        CancellationToken ct = default)
    {
        var user = await RequireUserAsync(identityId, ct);
        return await _userManager.CheckPasswordAsync(user, password);
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(
        Guid identityId,
        CancellationToken ct = default)
    {
        var user = await RequireUserAsync(identityId, ct);
        var roles = await _userManager.GetRolesAsync(user);
        return roles.ToArray();
    }

    public async Task<IdentityOperationResult> AddToRoleAsync(
        Guid identityId,
        string role,
        CancellationToken ct = default)
    {
        var user = await RequireUserAsync(identityId, ct);
        var result = await _userManager.AddToRoleAsync(user, role);
        return Map(result);
    }

    private async Task<User> RequireUserAsync(Guid identityId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return await _userManager.FindByIdAsync(identityId.ToString())
            ?? throw new InvalidOperationException($"Identity user '{identityId}' was not found.");
    }

    private static IdentityAccountSnapshot Map(User user) =>
        new(
            user.Id,
            user.Id,
            user.Email,
            user.PhoneNumber,
            user.EmailConfirmed,
            user.PhoneNumberConfirmed,
            !string.IsNullOrWhiteSpace(user.PasswordHash));

    private static IdentityOperationResult Map(IdentityResult result) =>
        result.Succeeded
            ? IdentityOperationResult.Success()
            : IdentityOperationResult.Failed(result.Errors.Select(error => error.Description));
}
