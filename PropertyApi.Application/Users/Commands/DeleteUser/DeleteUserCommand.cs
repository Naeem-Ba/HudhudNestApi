using MediatR;

namespace PropertyApi.Application.Users.Commands.DeleteUser;

/// <summary>
/// "Delete my own account" — UserId always comes from the authenticated principal
/// (see UsersController.DeleteAccount), never from client-supplied input, so this command
/// can only ever act on the caller's own account.
///
/// CurrentPassword re-authenticates a password-based account before this irreversible
/// action proceeds (see DeleteUserCommandHandler) — required only when the identity actually
/// has a password (IdentityAccountSnapshot.HasPassword); a social-login-only account has
/// nothing to verify it against, so it is left null for those callers.
/// </summary>
public sealed record DeleteUserCommand(
    Guid UserId,
    string? CurrentPassword = null,
    string? IpAddress = null) : IRequest<DeleteUserResult>;

public sealed record DeleteUserResult
{
    public bool Success { get; init; }
    public bool NotFound { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public static DeleteUserResult Ok() => new()
    {
        Success = true
    };

    public static DeleteUserResult UserNotFound() => new()
    {
        Success = false,
        NotFound = true
    };

    public static DeleteUserResult Fail(IEnumerable<string> errors) => new()
    {
        Success = false,
        Errors = errors.ToArray()
    };
}

