using HudhudNestApi.Application.Auth.Models;

namespace HudhudNestApi.Application.Auth.Interfaces;

/// <summary>
/// Identity capabilities required by the delete-user workflow.
/// </summary>
public interface IDeleteUserIdentityService
{
    Task<IdentityAccountSnapshot?> FindByIdAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityOperationResult> UpdateSecurityStampAsync(
        Guid identityId,
        CancellationToken ct = default);

    Task<IdentityOperationResult> SoftDeleteAsync(
        Guid identityId,
        DateTime deletedAtUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Irreversibly scrubs the Identity-layer credentials so the real email/phone number are
    /// freed up (no longer unique-constrained by a "deleted" account) and no login path can
    /// reach this identity again:
    ///
    ///   - Email/UserName (and their Normalized* counterparts) become
    ///     <paramref name="anonymizedEmail"/>; EmailConfirmed is reset to false.
    ///   - PhoneNumber/NormalizedPhoneNumber/PhoneNumberLookupHash are cleared;
    ///     PhoneNumberConfirmed is reset to false.
    ///   - Every external login (Google/Apple/...) is removed, so a later sign-in attempt
    ///     with that same provider account cannot resolve back to this identity, and a
    ///     future new registration with the same provider account is not blocked by a
    ///     stale (LoginProvider, ProviderKey) row pointing at a deleted user.
    ///
    /// Deliberately does not touch PasswordHash, SecurityStamp, or the soft-delete flags —
    /// callers combine this with <see cref="UpdateSecurityStampAsync"/> and
    /// <see cref="SoftDeleteAsync"/> (see DeleteUserCommandHandler).
    /// </summary>
    Task<IdentityOperationResult> AnonymizeCredentialsAsync(
        Guid identityId,
        string anonymizedEmail,
        DateTime utcNow,
        CancellationToken ct = default);
}
