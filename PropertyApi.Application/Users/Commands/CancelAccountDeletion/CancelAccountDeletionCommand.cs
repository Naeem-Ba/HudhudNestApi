using MediatR;

namespace PropertyApi.Application.Users.Commands.CancelAccountDeletion;

/// <summary>
/// Cancels a pending deletion request during its delay window (Finding F7,
/// docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md). UserId always comes from the authenticated
/// principal, never from client-supplied input, mirroring every other <c>/me</c> command in
/// this codebase.
/// </summary>
public sealed record CancelAccountDeletionCommand(Guid UserId) : IRequest<CancelAccountDeletionResult>;

public sealed record CancelAccountDeletionResult
{
    public bool Success { get; init; }
    public bool NotFound { get; init; }
    public bool NoPendingRequest { get; init; }

    public static CancelAccountDeletionResult Ok() => new() { Success = true };

    public static CancelAccountDeletionResult UserNotFound() => new()
    {
        Success = false,
        NotFound = true
    };

    public static CancelAccountDeletionResult NoPending() => new()
    {
        Success = false,
        NoPendingRequest = true
    };
}
