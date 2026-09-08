using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Users.Interfaces;

namespace PropertyApi.Application.Users.Commands.RequestDeleteUser;

/// <summary>
/// Finding F7 (docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md): the public "delete my account"
/// entry point now schedules deletion for later, cancellable execution instead of anonymizing
/// immediately. Re-authentication mirrors DeleteUserCommandHandler's own step exactly (kept as a
/// small, deliberate duplication rather than refactoring that already-tested class again for a
/// ~20-line block — see DeleteUserCommandHandler's class doc comment). The actual anonymization
/// happens later, unattended, via DeleteUserCommandHandler.ExecuteScheduledDeletionAsync, called
/// by AccountDeletionSweepHostedService once DeletionScheduledFor arrives.
/// </summary>
public sealed class RequestDeleteUserCommandHandler
    : IRequestHandler<RequestDeleteUserCommand, RequestDeleteUserResult>
{
    private readonly IDeleteUserIdentityService _identity;
    private readonly ILoginIdentityService _login;
    private readonly IUserAccountRepository _accounts;
    private readonly IAccountDeletionSettings _settings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<RequestDeleteUserCommandHandler> _logger;

    public RequestDeleteUserCommandHandler(
        IDeleteUserIdentityService identity,
        ILoginIdentityService login,
        IUserAccountRepository accounts,
        IAccountDeletionSettings settings,
        IUnitOfWork unitOfWork,
        ILogger<RequestDeleteUserCommandHandler> logger)
    {
        _identity = identity;
        _login = login;
        _accounts = accounts;
        _settings = settings;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<RequestDeleteUserResult> Handle(
        RequestDeleteUserCommand request,
        CancellationToken cancellationToken)
    {
        var identity =
            await _identity.FindByIdAsync(
                request.UserId,
                cancellationToken);

        if (identity is null ||
            identity.IsDeleted)
        {
            return RequestDeleteUserResult.UserNotFound();
        }

        /*
         * Re-authentication -- same rule as DeleteUserCommandHandler.Handle: only meaningful
         * for an account that actually has a password.
         */
        if (identity.HasPassword)
        {
            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            {
                return RequestDeleteUserResult.Fail(new[] { "CURRENT_PASSWORD_REQUIRED" });
            }

            var verification =
                await _login.VerifyPasswordWithLockoutAsync(
                    identity.IdentityId,
                    request.CurrentPassword,
                    cancellationToken);

            if (verification == LoginPasswordVerificationResult.LockedOut)
            {
                return RequestDeleteUserResult.Fail(new[] { "ACCOUNT_LOCKED" });
            }

            if (verification != LoginPasswordVerificationResult.Success)
            {
                return RequestDeleteUserResult.Fail(new[] { "INVALID_CURRENT_PASSWORD" });
            }
        }

        var account =
            await _accounts.GetByIdAsync(
                identity.UserAccountId,
                cancellationToken);

        if (account is null)
        {
            _logger.LogWarning(
                "UserAccount profile was not found for identity {IdentityId} while requesting account deletion.",
                identity.IdentityId);

            return RequestDeleteUserResult.UserNotFound();
        }

        var now = DateTime.UtcNow;

        // RequestDeletion is idempotent (see its own doc comment): a second call while a
        // request is already pending returns the existing schedule rather than pushing it out.
        var scheduledFor = account.RequestDeletion(
            TimeSpan.FromDays(_settings.DelayDays),
            now);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Account deletion requested for identity {IdentityId}, scheduled for {ScheduledFor:O}.",
            identity.IdentityId,
            scheduledFor);

        return RequestDeleteUserResult.Ok(scheduledFor);
    }
}
