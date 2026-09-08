using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Users.Interfaces;

namespace PropertyApi.Application.Users.Commands.CancelAccountDeletion;

/// <summary>
/// Finding F7 (docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md). No re-authentication is required
/// to cancel -- an authenticated session is already the same or a weaker bar than the one that
/// started the request, and requiring the password again here would only make it *harder* to
/// back out of a deletion than to start one, which is the wrong incentive for a cancellation
/// path that exists specifically to be low-friction during the grace window.
/// </summary>
public sealed class CancelAccountDeletionCommandHandler
    : IRequestHandler<CancelAccountDeletionCommand, CancelAccountDeletionResult>
{
    private readonly IUserAccountRepository _accounts;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CancelAccountDeletionCommandHandler> _logger;

    public CancelAccountDeletionCommandHandler(
        IUserAccountRepository accounts,
        IUnitOfWork unitOfWork,
        ILogger<CancelAccountDeletionCommandHandler> logger)
    {
        _accounts = accounts;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<CancelAccountDeletionResult> Handle(
        CancelAccountDeletionCommand request,
        CancellationToken cancellationToken)
    {
        // UserAccounts.Id == Users.Id (shared primary key -- see UserAccountConfiguration.cs),
        // and request.UserId is always the authenticated principal's own id, so there is no
        // separate Identity-layer lookup needed here, unlike DeleteUserCommandHandler (which
        // needs IdentityAccountSnapshot for HasPassword/re-authentication -- not needed here).
        var account =
            await _accounts.GetByIdAsync(
                request.UserId,
                cancellationToken);

        if (account is null || account.IsDeleted)
        {
            return CancelAccountDeletionResult.UserNotFound();
        }

        if (!account.HasPendingDeletionRequest)
        {
            return CancelAccountDeletionResult.NoPending();
        }

        account.CancelDeletionRequest(DateTime.UtcNow);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Pending account deletion cancelled for account {AccountId}.",
            account.Id);

        return CancelAccountDeletionResult.Ok();
    }
}
