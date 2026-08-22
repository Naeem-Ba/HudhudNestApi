using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Infrastructure.Identity.Entities;

namespace PropertyApi.Infrastructure.Identity.Services;

public sealed class IdentityCredentialService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IPhoneNumberLookupHasher _phoneLookupHasher;
    private readonly IPhoneNumberNormalizer _phoneNumberNormalizer;

    public IdentityCredentialService(
        UserManager<ApplicationUser> users,
        IPhoneNumberLookupHasher phoneLookupHasher,
        IPhoneNumberNormalizer phoneNumberNormalizer)
    {
        _users = users;
        _phoneLookupHasher = phoneLookupHasher;
        _phoneNumberNormalizer = phoneNumberNormalizer;
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

    /// <summary>
    /// Sets (or clears) a user's phone number.
    ///
    /// SECURITY/INTEGRITY FIX: this used to write <c>phone.Trim()</c> straight into
    /// NormalizedPhoneNumber and derive the lookup hash from that raw string, with no
    /// uniqueness check. Three things went wrong:
    ///
    ///   • No E.164 normalization, while the OTP flow normalizes through
    ///     IPhoneNumberNormalizer. "+963911234567" and "00963911234567" are the same
    ///     real number but two different stored values, so the unique index could not
    ///     see the collision and one phone ended up owning two accounts -- an
    ///     assumption phone login and phone password reset both depend on.
    ///   • NormalizedPhoneNumber is capped at 16 characters, so any longer input
    ///     surfaced as an unhandled DbUpdateException, i.e. HTTP 500 instead of 400.
    ///   • A number already held by another account hit the unique index the same way:
    ///     HTTP 500 instead of 409.
    ///
    /// The normalizer is now the single source of truth for what a phone number looks
    /// like, and both failure modes are returned as stable error codes the API layer
    /// can translate. PhoneNumberConfirmed is still reset, so a number set here is
    /// unverified until it passes the OTP flow.
    /// </summary>
    public async Task<IdentityOperationResult> SetPhoneNumberAsync(
        Guid id, string? phone, DateTime changedAtUtc, CancellationToken ct)
    {
        var user = await Load(id, ct);

        string? normalized = null;
        if (!string.IsNullOrWhiteSpace(phone))
        {
            var normalization = _phoneNumberNormalizer.Normalize(phone);
            if (!normalization.Succeeded)
            {
                return IdentityOperationResult.Failed(
                    normalization.ErrorCode ?? "PHONE_NUMBER_INVALID");
            }

            normalized = normalization.Value;
        }

        if (string.Equals(user.NormalizedPhoneNumber, normalized, StringComparison.Ordinal))
            return IdentityOperationResult.Success();

        string? lookupHash = null;
        if (normalized is not null)
        {
            lookupHash = _phoneLookupHasher.Compute(normalized);

            // Explicit pre-check so a collision is a 409 with a stable code rather than
            // a unique-index violation escaping as a 500. The index still backs this up
            // against the race between check and save.
            var alreadyTaken = await _users.Users
                .AnyAsync(
                    other => other.PhoneNumberLookupHash == lookupHash && other.Id != user.Id,
                    ct);

            if (alreadyTaken)
                return IdentityOperationResult.Failed("PHONE_NUMBER_ALREADY_IN_USE");
        }

        user.PhoneNumber = normalized;
        user.NormalizedPhoneNumber = normalized;
        user.PhoneNumberLookupHash = lookupHash;
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
