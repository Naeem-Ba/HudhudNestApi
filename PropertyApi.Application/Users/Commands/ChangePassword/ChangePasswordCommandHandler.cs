using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Audit.Constants;

namespace PropertyApi.Application.Users.Commands.ChangePassword;

public sealed class ChangePasswordCommandHandler
    : IRequestHandler<
        ChangePasswordCommand,
        ChangePasswordResult>
{
    private readonly IChangePasswordIdentityService _identity;

    private readonly IAuditLogService _auditLogs;

    private readonly ILogger<ChangePasswordCommandHandler>
        _logger;

    public ChangePasswordCommandHandler(
        IChangePasswordIdentityService identity,
        IAuditLogService auditLogs,
        ILogger<ChangePasswordCommandHandler> logger)
    {
        _identity = identity;
        _auditLogs = auditLogs;
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

                        timestamp =
                            changedAtUtc
                    }),

            ct:
                cancellationToken);

        return ChangePasswordResult.Ok();
    }
}
