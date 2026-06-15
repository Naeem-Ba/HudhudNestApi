using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Application.Users.Commands.ChangePassword;

public sealed class ChangePasswordCommandHandler
    : IRequestHandler<ChangePasswordCommand, ChangePasswordResult>
{
    private readonly IIdentityUserService _identityUsers;
    private readonly ILogger<ChangePasswordCommandHandler> _logger;

    public ChangePasswordCommandHandler(
        IIdentityUserService identityUsers,
        ILogger<ChangePasswordCommandHandler> logger)
    {
        _identityUsers = identityUsers;
        _logger = logger;
    }

    public async Task<ChangePasswordResult> Handle(
        ChangePasswordCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _identityUsers.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null || user.IsDeleted)
            return ChangePasswordResult.UserNotFound();

        var result = await _identityUsers.ChangePasswordAsync(
            user,
            request.CurrentPassword,
            request.NewPassword,
            cancellationToken);

        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "Password change failed for user {UserId}. Errors: {Errors}",
                request.UserId,
                string.Join(", ", result.Errors));

            return ChangePasswordResult.Fail(result.Errors);
        }

        user.UpdatedAt = DateTime.UtcNow;

        var updateResult = await _identityUsers.UpdateAsync(user, cancellationToken);
        if (!updateResult.Succeeded)
        {
            _logger.LogWarning(
                "User metadata update failed after password change for user {UserId}. Errors: {Errors}",
                request.UserId,
                string.Join(", ", updateResult.Errors));
        }

        var stampResult = await _identityUsers.UpdateSecurityStampAsync(user, cancellationToken);
        if (!stampResult.Succeeded)
        {
            _logger.LogWarning(
                "Security stamp update failed after password change for user {UserId}. Errors: {Errors}",
                request.UserId,
                string.Join(", ", stampResult.Errors));
        }

        return ChangePasswordResult.Ok();
    }
}

