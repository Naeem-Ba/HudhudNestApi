using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Application.Users.Commands.DeleteUser;

public sealed class DeleteUserCommandHandler
    : IRequestHandler<DeleteUserCommand, DeleteUserResult>
{
    private readonly IIdentityUserService _identityUsers;
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(
        IIdentityUserService identityUsers,
        ILogger<DeleteUserCommandHandler> logger)
    {
        _identityUsers = identityUsers;
        _logger = logger;
    }

    public async Task<DeleteUserResult> Handle(
        DeleteUserCommand request,
        CancellationToken cancellationToken)
    {
        var user = await _identityUsers.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null || user.IsDeleted)
            return DeleteUserResult.UserNotFound();

        user.IsDeleted = true;
        user.DeletedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        var stampResult = await _identityUsers.UpdateSecurityStampAsync(user, cancellationToken);
        if (!stampResult.Succeeded)
        {
            _logger.LogWarning(
                "Security stamp update failed while deleting user {UserId}. Errors: {Errors}",
                request.UserId,
                string.Join(", ", stampResult.Errors));
        }

        var result = await _identityUsers.UpdateAsync(user, cancellationToken);
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "Soft delete failed for user {UserId}. Errors: {Errors}",
                request.UserId,
                string.Join(", ", result.Errors));

            return DeleteUserResult.Fail(result.Errors);
        }

        return DeleteUserResult.Ok();
    }
}

