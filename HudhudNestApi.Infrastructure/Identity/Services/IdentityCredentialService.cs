using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Infrastructure.Identity.Entities;

namespace HudhudNestApi.Infrastructure.Identity.Services;

public sealed class IdentityCredentialService
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IPhoneNumberLookupHasher _phoneLookupHasher;
    private readonly IPhoneNumberNormalizer _phoneNumberNormalizer;
    private readonly IUserSecurityStampCacheInvalidator _stampCache;

    public IdentityCredentialService(
        UserManager<ApplicationUser> users,
        IPhoneNumberLookupHasher phoneLookupHasher,
        IPhoneNumberNormalizer phoneNumberNormalizer,
        IUserSecurityStampCacheInvalidator stampCache)
    {
        _users = users;
        _phoneLookupHasher = phoneLookupHasher;
        _phoneNumberNormalizer = phoneNumberNormalizer;
        _stampCache = stampCache;
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
        var result = Result(await _users.UpdateSecurityStampAsync(user));

        // JwtBearer validates the stamp claim against a 5-minute cached snapshot
        // (CachedSecurityStampValidator). Rotating the stamp without dropping that entry leaves every
        // access token issued before the rotation usable for up to five more minutes -- exactly the
        // window a password change / reset exists to close (security audit 2026-10-03, F-03).
        if (result.Succeeded)
        {
            await _stampCache.InvalidateAsync(id, ct);
        }

        return result;
    }

    public async Task<string> GeneratePasswordResetTokenAsync(Guid id, CancellationToken ct) =>
        await _users.GeneratePasswordResetTokenAsync(await Load(id, ct));

    public async Task<IdentityOperationResult> ResetPasswordAsync(
        Guid id, string token, string password, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var user = await Load(id, ct);
        var result = Result(await _users.ResetPasswordAsync(user, token, password));
        if (!result.Succeeded)
        {
            return result;
        }

        // The reset token proves control of the mailbox, so it is safe to lift a lockout the
        // owner just triggered by mistyping; otherwise "reset your password" would leave
        // them locked out for the rest of the lockout window. Matches the phone reset flow.
        await _users.ResetAccessFailedCountAsync(user);
        await _users.SetLockoutEndDateAsync(user, null);
        return result;
    }

    public async Task<IdentityOperationResult> ChangePasswordAsync(
        Guid id, string currentPassword, string newPassword, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPassword);
        var user = await Load(id, ct);

        // UserManager.ChangePasswordAsync verifies the current password without ever touching the
        // access-failure counter, and the endpoint has no rate-limit policy: a stolen access token
        // could guess the real password without limit (security audit 2026-10-03, F-02). Count the
        // failure exactly like every login path does, and refuse while the account is locked out.
        if (await _users.IsLockedOutAsync(user))
        {
            return IdentityOperationResult.Failed("Account is temporarily locked. Try again later.");
        }

        if (!await _users.CheckPasswordAsync(user, currentPassword))
        {
            await _users.AccessFailedAsync(user);
            return Result(IdentityResult.Failed(_users.ErrorDescriber.PasswordMismatch()));
        }

        if (user.AccessFailedCount > 0)
        {
            await _users.ResetAccessFailedCountAsync(user);
        }

        return Result(await _users.ChangePasswordAsync(user, currentPassword, newPassword));
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

    /// <summary>
    /// See <see cref="IDeleteUserIdentityService.AnonymizeCredentialsAsync"/> for the
    /// contract. Login removal happens first and its own failures are folded into the
    /// aggregate result instead of short-circuiting, so one unremovable login link does not
    /// leave the email/phone still exposed — the caller (DeleteUserCommandHandler) treats
    /// any failure here as blocking and rolls back the whole deletion transaction.
    /// </summary>
    public async Task<IdentityOperationResult> AnonymizeCredentialsAsync(
        Guid id, string anonymizedEmail, DateTime utcNow, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(anonymizedEmail);

        var user = await Load(id, ct);
        var errors = new List<string>();

        var logins = await _users.GetLoginsAsync(user);
        foreach (var login in logins)
        {
            var removeResult = await _users.RemoveLoginAsync(
                user,
                login.LoginProvider,
                login.ProviderKey);

            if (!removeResult.Succeeded)
            {
                errors.AddRange(removeResult.Errors.Select(e => e.Description));
            }
        }

        var normalizedEmail = anonymizedEmail.ToUpperInvariant();

        user.Email = anonymizedEmail;
        user.NormalizedEmail = normalizedEmail;
        user.EmailConfirmed = false;
        user.UserName = anonymizedEmail;
        user.NormalizedUserName = normalizedEmail;
        user.PhoneNumber = null;
        user.NormalizedPhoneNumber = null;
        user.PhoneNumberLookupHash = null;
        user.PhoneNumberConfirmed = false;
        user.UpdatedAt = utcNow;

        var updateResult = await _users.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            errors.AddRange(updateResult.Errors.Select(e => e.Description));
        }

        return errors.Count == 0
            ? IdentityOperationResult.Success()
            : IdentityOperationResult.Failed(errors);
    }

    private Task<ApplicationUser> Load(Guid id, CancellationToken ct) =>
        IdentityAdapterMapping.RequireAsync(_users, id, ct);

    private static IdentityOperationResult Result(IdentityResult result) =>
        IdentityAdapterMapping.Result(result);
}
