using MediatR;

namespace PropertyApi.Application.Users.Commands.RequestDeleteUser;

/// <summary>
/// Starts the account-deletion delay window (Finding F7,
/// docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md) — the public "delete my account" entry point
/// (see UsersController.DeleteAccount). UserId always comes from the authenticated principal,
/// never from client-supplied input, mirroring DeleteUserCommand's own contract.
///
/// CurrentPassword re-authenticates a password-based account before the request is accepted,
/// for the same reason and under the same rule as DeleteUserCommand.CurrentPassword — required
/// only when the identity actually has a password.
/// </summary>
public sealed record RequestDeleteUserCommand(
    Guid UserId,
    string? CurrentPassword = null) : IRequest<RequestDeleteUserResult>;

public sealed record RequestDeleteUserResult
{
    public bool Success { get; init; }
    public bool NotFound { get; init; }
    public DateTime? ScheduledFor { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public static RequestDeleteUserResult Ok(DateTime scheduledFor) => new()
    {
        Success = true,
        ScheduledFor = scheduledFor
    };

    public static RequestDeleteUserResult UserNotFound() => new()
    {
        Success = false,
        NotFound = true
    };

    public static RequestDeleteUserResult Fail(IEnumerable<string> errors) => new()
    {
        Success = false,
        Errors = errors.ToArray()
    };
}
