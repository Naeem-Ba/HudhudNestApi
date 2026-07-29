using Microsoft.AspNetCore.Identity;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Infrastructure.Identity.Entities;

namespace PropertyApi.Infrastructure.Identity.Services;

public sealed class IdentityCredentialService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IPhoneNumberLookupHasher _phoneLookupHasher;

    public IdentityCredentialService(
        UserManager<ApplicationUser> users,
        IPhoneNumberLookupHasher phoneLookupHasher)
    {
        _users = users;
        _phoneLookupHasher = phoneLookupHasher;
    }

    public async Task<IdentityOperationResult> ConfirmPhoneNumberAsync(
        Guid id, DateTime confirmedAtUtc, CancellationToken ct)
    {
        var user = await Load(id, ct);
        if (user.PhoneNumberConfirmed) return IdentityOperationResult.Success();
        user.PhoneNumberConfirmed = true;
        user.UpdatedAt = confirmedAtUtc;
        return Result(await _users.UpdateAsync(user));
    }

    public async Task<IdentityOperationResult> UpdateSecurityStampAsync(Guid id, CancellationToken ct)
    {
        var user = await Load(id, ct);
        return Result(await _users.UpdateSecurityStampAsync(user));
    }

    public async Task<string> GeneratePasswordResetTokenAsync(Guid id, CancellationToken ct) =>
        await _users.GeneratePasswordResetTokenAsync(await Load(id, ct));

    public async Task<IdentityOperationResult> ResetPasswordAsync(
        Guid id, string token, string password, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return Result(await _users.ResetPasswordAsync(await Load(id, ct), token, password));
    }

    public async Task<IdentityOperationResult> ChangePasswordAsync(
        Guid id, string currentPassword, string newPassword, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);
        return Result(await _users.ChangePasswordAsync(
            await Load(id, ct), currentPassword, newPassword));
    }

    public async Task<IdentityOperationResult> RecordCredentialChangeAsync(
        Guid id, DateTime changedAtUtc, CancellationToken ct)
    {
        var user = await Load(id, ct);
        user.UpdatedAt = changedAtUtc;
        return Result(await _users.UpdateAsync(user));
    }

    public async Task<IdentityOperationResult> SetEmailAsync(
        Guid id, string email, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return Result(await _users.SetEmailAsync(await Load(id, ct), email));
    }

    public async Task<string> GenerateEmailConfirmationTokenAsync(Guid id, CancellationToken ct) =>
        await _users.GenerateEmailConfirmationTokenAsync(await Load(id, ct));

    public async Task<IdentityOperationResult> ConfirmEmailAsync(
        Guid id, string token, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return Result(await _users.ConfirmEmailAsync(await Load(id, ct), token));
    }

    public async Task<IdentityOperationResult> SetPhoneNumberAsync(
        Guid id, string? phone, DateTime changedAtUtc, CancellationToken ct)
    {
        var user = await Load(id, ct);
        var normalized = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (string.Equals(user.PhoneNumber, normalized, StringComparison.Ordinal))
            return IdentityOperationResult.Success();
        user.PhoneNumber = normalized;
        user.NormalizedPhoneNumber = normalized;
        user.PhoneNumberLookupHash = normalized is null
            ? null
            : _phoneLookupHasher.Compute(normalized);
        user.PhoneNumberConfirmed = false;
        user.UpdatedAt = changedAtUtc;
        return Result(await _users.UpdateAsync(user));
    }

    public async Task<IdentityOperationResult> SoftDeleteAsync(
        Guid id, DateTime deletedAtUtc, CancellationToken ct)
    {
        var user = await Load(id, ct);
        if (user.IsDeleted) return IdentityOperationResult.Success();
        user.IsDeleted = true;
        user.DeletedAt = deletedAtUtc;
        user.UpdatedAt = deletedAtUtc;
        return Result(await _users.UpdateAsync(user));
    }

    private Task<ApplicationUser> Load(Guid id, CancellationToken ct) =>
        IdentityAdapterMapping.RequireAsync(_users, id, ct);

    private static IdentityOperationResult Result(IdentityResult result) =>
        IdentityAdapterMapping.Result(result);
}
