using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Domain.Audit.Constants;

namespace PropertyApi.Application.Users.Commands.DeleteUser;

/// <summary>
/// Account deletion — the real, server-side kind (Phase 5 / GDPR erasure), not a UI
/// affordance backed by nothing. Ordering follows the same "sensitive Identity + UserAccount
/// mutation together" shape as UpdateUserCommandHandler, plus the "invalidate every existing
/// session" shape LogoutCommandHandler/ChangePasswordCommandHandler already establish:
///
///   1. Re-authenticate (password-based accounts only — see DeleteUserCommand's doc comment).
///   2. Anonymize + soft-delete the Identity row and anonymize the UserAccount profile row,
///      atomically (one DB transaction: either both change or neither does).
///   3. Best-effort, non-transactional cleanup that cannot leave the account half-deleted
///      even if it fails: revoke every refresh token, invalidate the cached security stamp,
///      delete the Cloudinary avatar, write the audit trail.
///
/// See docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md for the full data-dependency map this
/// implements and the retention/business-decision boundaries it deliberately does NOT cross
/// (e.g. properties/agencies/reviews owned by the deleted user are preserved, not deleted).
/// </summary>
public sealed class DeleteUserCommandHandler
    : IRequestHandler<DeleteUserCommand, DeleteUserResult>
{
    private readonly IDeleteUserIdentityService _identity;
    private readonly ILoginIdentityService _login;
    private readonly IUserAccountRepository _accounts;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUserSecurityStampCacheInvalidator _securityStampCacheInvalidator;
    private readonly IMediaStorageService _storage;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogService _auditLogs;
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(
        IDeleteUserIdentityService identity,
        ILoginIdentityService login,
        IUserAccountRepository accounts,
        IRefreshTokenRepository refreshTokens,
        IUserSecurityStampCacheInvalidator securityStampCacheInvalidator,
        IMediaStorageService storage,
        IUnitOfWork unitOfWork,
        IAuditLogService auditLogs,
        ILogger<DeleteUserCommandHandler> logger)
    {
        _identity = identity;
        _login = login;
        _accounts = accounts;
        _refreshTokens = refreshTokens;
        _securityStampCacheInvalidator = securityStampCacheInvalidator;
        _storage = storage;
        _unitOfWork = unitOfWork;
        _auditLogs = auditLogs;
        _logger = logger;
    }

    public async Task<DeleteUserResult> Handle(
        DeleteUserCommand request,
        CancellationToken cancellationToken)
    {
        var identity =
            await _identity.FindByIdAsync(
                request.UserId,
                cancellationToken);

        if (identity is null ||
            identity.IsDeleted)
        {
            return DeleteUserResult.UserNotFound();
        }

        /*
         * Re-authentication (§6/§25 of the account-deletion spec): only meaningful for an
         * account that actually has a password. A social-login-only account has nothing to
         * verify a submitted password against — see IdentityAccountSnapshot.HasPassword and
         * DeleteUserCommand's doc comment for why that is not treated as a bypass.
         */
        if (identity.HasPassword)
        {
            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            {
                return DeleteUserResult.Fail(new[] { "CURRENT_PASSWORD_REQUIRED" });
            }

            var verification =
                await _login.VerifyPasswordWithLockoutAsync(
                    identity.IdentityId,
                    request.CurrentPassword,
                    cancellationToken);

            if (verification == LoginPasswordVerificationResult.LockedOut)
            {
                return DeleteUserResult.Fail(new[] { "ACCOUNT_LOCKED" });
            }

            if (verification != LoginPasswordVerificationResult.Success)
            {
                return DeleteUserResult.Fail(new[] { "INVALID_CURRENT_PASSWORD" });
            }
        }

        var account =
            await _accounts.GetByIdAsync(
                identity.UserAccountId,
                cancellationToken);

        if (account is null)
        {
            _logger.LogWarning(
                "UserAccount profile was not found for identity {IdentityId} during account deletion.",
                identity.IdentityId);

            return DeleteUserResult.UserNotFound();
        }

        var now =
            DateTime.UtcNow;

        // Read before Anonymize() clears it — this is the only handle we have on the
        // Cloudinary asset once the DB row no longer carries it.
        var previousProfileImagePublicId =
            account.ProfileImagePublicId;

        var anonymizedEmail =
            BuildAnonymizedEmail(identity.IdentityId);

        await _unitOfWork.BeginTransactionAsync(
            cancellationToken);

        try
        {
            var credentialsResult =
                await _identity.AnonymizeCredentialsAsync(
                    identity.IdentityId,
                    anonymizedEmail,
                    now,
                    cancellationToken);

            if (!credentialsResult.Succeeded)
            {
                _logger.LogWarning(
                    "Credential anonymization failed for identity {IdentityId}. Errors: {Errors}",
                    identity.IdentityId,
                    string.Join(", ", credentialsResult.Errors));

                await _unitOfWork.RollbackTransactionAsync(
                    CancellationToken.None);

                return DeleteUserResult.Fail(credentialsResult.Errors);
            }

            /*
             * Rotate the security stamp so existing access tokens can no longer pass the
             * per-request stamp check (see CachedSecurityStampValidator). Preserve the
             * previous behavior: a stamp-update failure is logged but does not abort the
             * deletion — IsDeleted alone already fails that same check.
             */
            var stampResult =
                await _identity.UpdateSecurityStampAsync(
                    identity.IdentityId,
                    cancellationToken);

            if (!stampResult.Succeeded)
            {
                _logger.LogWarning(
                    "Security stamp update failed while deleting identity {IdentityId}. Errors: {Errors}",
                    identity.IdentityId,
                    string.Join(", ", stampResult.Errors));
            }

            var deleteResult =
                await _identity.SoftDeleteAsync(
                    identity.IdentityId,
                    now,
                    cancellationToken);

            if (!deleteResult.Succeeded)
            {
                _logger.LogWarning(
                    "Soft delete failed for identity {IdentityId}. Errors: {Errors}",
                    identity.IdentityId,
                    string.Join(", ", deleteResult.Errors));

                await _unitOfWork.RollbackTransactionAsync(
                    CancellationToken.None);

                return DeleteUserResult.Fail(deleteResult.Errors);
            }

            account.Anonymize(now);

            await _unitOfWork.SaveChangesAsync(
                cancellationToken);

            await _unitOfWork.CommitTransactionAsync(
                cancellationToken);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(
                CancellationToken.None);

            throw;
        }

        /*
         * Everything below is deliberately outside the transaction: the account is already
         * deleted at this point (committed), and none of these steps can be rolled back
         * anyway (a revoked refresh token, an invalidated cache entry, or a deleted Cloudinary
         * asset does not "undo"). Same pattern as LogoutCommandHandler (always invalidate the
         * cache) and UploadUserAvatarCommandHandler (best-effort external-storage cleanup).
         */
        await _refreshTokens.RevokeActiveTokensForUserAsync(
            identity.IdentityId,
            now,
            request.IpAddress,
            cancellationToken);

        await _securityStampCacheInvalidator.InvalidateAsync(
            identity.IdentityId,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(previousProfileImagePublicId))
        {
            try
            {
                await _storage.DeleteImageAsync(
                    previousProfileImagePublicId,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete profile image from storage after account deletion. IdentityId={IdentityId}",
                    identity.IdentityId);
            }
        }

        await _auditLogs.LogAsync(
            userId: identity.IdentityId,
            action: AuditActions.AccountDeletionCompleted,
            ipAddress: request.IpAddress,
            oldValue: null,
            newValue: JsonSerializer.Serialize(
                new
                {
                    userId = identity.IdentityId,
                    timestamp = now
                }),
            ct: cancellationToken);

        return DeleteUserResult.Ok();
    }

    /// <summary>
    /// A stable, unmistakably-non-deliverable, per-account-unique placeholder — frees the
    /// real email/username for reuse (they carry unique indexes) without ever colliding
    /// across two deleted accounts. ".invalid" is the reserved TLD for exactly this purpose
    /// (RFC 2606) — never resolvable, so nothing could ever be sent to it by mistake.
    /// </summary>
    private static string BuildAnonymizedEmail(Guid identityId)
        => $"deleted-{identityId:N}@deleted.invalid";
}
