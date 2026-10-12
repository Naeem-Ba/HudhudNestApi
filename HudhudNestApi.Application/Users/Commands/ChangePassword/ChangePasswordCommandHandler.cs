using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Audit.Constants;

namespace HudhudNestApi.Application.Users.Commands.ChangePassword;

public sealed class ChangePasswordCommandHandler
    : IRequestHandler<
        ChangePasswordCommand,
        ChangePasswordResult>
{
    private readonly IChangePasswordIdentityService _identity;

    private readonly IAuditLogService _auditLogs;

    private readonly IRefreshTokenRepository _refreshTokens;

    private readonly ISecurityAlertDispatcher _securityAlerts;

    private readonly ILogger<ChangePasswordCommandHandler>
        _logger;

    public ChangePasswordCommandHandler(
        IChangePasswordIdentityService identity,
        IAuditLogService auditLogs,
        IRefreshTokenRepository refreshTokens,
        ISecurityAlertDispatcher securityAlerts,
        ILogger<ChangePasswordCommandHandler> logger)
    {
        _identity = identity;
        _auditLogs = auditLogs;
        _refreshTokens = refreshTokens;
        _securityAlerts = securityAlerts;
        _logger = logger;
    }

    public async Task<ChangePasswordResult> Handle(
        ChangePasswordCommand request,
        CancellationToken cancellationToken)
    {
        var identity =
            await _identity.FindByIdAsync(
                request.UserId,
                cancellationToken);

        if (identity is null ||
            identity.IsDeleted)
        {
            return ChangePasswordResult.UserNotFound();
        }

        var result =
            await _identity.ChangePasswordAsync(
                identity.IdentityId,
                request.CurrentPassword,
                request.NewPassword,
                cancellationToken);

        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "Password change failed for identity {IdentityId}. Errors: {Errors}",
                identity.IdentityId,
                string.Join(
                    ", ",
                    result.Errors));

            return ChangePasswordResult.Fail(
                result.Errors);
        }

        var changedAtUtc =
            DateTime.UtcNow;

        /*
         * Preserve the previous metadata behavior without exposing
         * the concrete Identity entity to the Application layer.
         */
        var metadataResult =
            await _identity.RecordCredentialChangeAsync(
                identity.IdentityId,
                changedAtUtc,
                cancellationToken);

        if (!metadataResult.Succeeded)
        {
            _logger.LogWarning(
                "Identity metadata update failed after password change for identity {IdentityId}. Errors: {Errors}",
                identity.IdentityId,
                string.Join(
                    ", ",
                    metadataResult.Errors));
        }

        /*
         * Preserve the previous explicit security-stamp rotation.
         */
        var stampResult =
            await _identity.UpdateSecurityStampAsync(
                identity.IdentityId,
                cancellationToken);

        if (!stampResult.Succeeded)
        {
            _logger.LogWarning(
                "Security stamp update failed after password change for identity {IdentityId}. Errors: {Errors}",
                identity.IdentityId,
                string.Join(
                    ", ",
                    stampResult.Errors));
        }

        /*
         * A password change must not leave pre-existing refresh tokens usable,
         * exactly like the reset-password flow (ResetPasswordCommandHandler) —
         * otherwise a session obtained before the change (e.g. by someone who
         * had briefly gained access) would keep working after the owner
         * "secures" the account by changing the password.
         */
        await _refreshTokens.RevokeActiveTokensForUserAsync(
            identity.IdentityId,
            changedAtUtc,
            request.IpAddress,
            cancellationToken);

        await _auditLogs.LogAsync(
            userId:
                identity.IdentityId,

            action:
                AuditActions.ChangePassword,

            ipAddress:
                request.IpAddress,

            oldValue:
                null,

            newValue:
                JsonSerializer.Serialize(
                    new
                    {
                        userId =
                            identity.IdentityId,

                        securityStampUpdated =
                            stampResult.Succeeded,

                        refreshTokensRevoked =
                            true,

                        timestamp =
                            changedAtUtc
                    }),

            ct:
                cancellationToken);

        QueuePasswordChangedConfirmation(identity);

        return ChangePasswordResult.Ok();
    }

    /// <summary>
    /// Tells the account owner the password was changed. The dispatcher picks the language from
    /// the account's saved preference; phone-only accounts have no address, so nothing is queued.
    /// </summary>
    private void QueuePasswordChangedConfirmation(IdentityAccountSnapshot identity)
    {
        if (string.IsNullOrWhiteSpace(identity.Email) ||
            string.IsNullOrWhiteSpace(identity.UserName))
        {
            return;
        }

        _securityAlerts.Enqueue(new SecurityAlertRequest(
            SecurityAlertKind.PasswordChangedConfirmation,
            UserEmail: identity.Email,
            UserName: identity.UserName));
    }
}
