using MediatR;

namespace PropertyApi.Application.Users.Commands.ChangePassword;

public sealed record ChangePasswordCommand(
    Guid UserId,
    string CurrentPassword,
    string NewPassword) : IRequest<ChangePasswordResult>;

public sealed record ChangePasswordResult
{
    public bool Success { get; init; }
    public bool NotFound { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public static ChangePasswordResult Ok() => new()
    {
        Success = true
    };

    public static ChangePasswordResult UserNotFound() => new()
    {
        Success = false,
        NotFound = true
    };

    public static ChangePasswordResult Fail(IEnumerable<string> errors) => new()
    {
        Success = false,
        Errors = errors.ToArray()
    };
}

