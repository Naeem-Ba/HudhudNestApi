using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Application.Users.Commands.DeleteUser;

public sealed class DeleteUserCommandHandler
    : IRequestHandler<DeleteUserCommand, DeleteUserResult>
{
    private readonly IPureIdentityService _identity;
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(
        IPureIdentityService identity,
        ILogger<DeleteUserCommandHandler> logger)
    {
        _identity = identity;
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

        var now =
            DateTime.UtcNow;

        /*
         * Rotate the security stamp first so existing access tokens
         * can no longer rely on the previous authentication stamp.
         *
         * Preserve the existing behavior:
         * a stamp-update failure is logged but does not prevent
         * the soft-delete attempt.
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
                string.Join(
                    ", ",
                    stampResult.Errors));
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
                string.Join(
                    ", ",
                    deleteResult.Errors));

            return DeleteUserResult.Fail(
                deleteResult.Errors);
        }

        return DeleteUserResult.Ok();
    }
}