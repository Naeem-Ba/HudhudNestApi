using MediatR;

namespace PropertyApi.Application.Users.Commands.DeleteUser;

public sealed record DeleteUserCommand(Guid UserId) : IRequest<DeleteUserResult>;

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

