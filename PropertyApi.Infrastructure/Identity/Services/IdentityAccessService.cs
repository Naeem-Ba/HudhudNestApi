using Microsoft.AspNetCore.Identity;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Infrastructure.Identity.Entities;

namespace PropertyApi.Infrastructure.Identity.Services;

public sealed class IdentityAccessService
{
    private readonly UserManager<ApplicationUser> _users;
    public IdentityAccessService(UserManager<ApplicationUser> users) => _users = users;

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
