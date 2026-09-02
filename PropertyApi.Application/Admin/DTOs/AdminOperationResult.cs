namespace PropertyApi.Application.Admin.DTOs;

public sealed record AdminOperationResult
{
    public bool Succeeded { get; init; }
    public bool NotFound { get; init; }
    public bool Conflict { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public static AdminOperationResult Ok(string message) => new()
    {
        Succeeded = true,
        Message = message
    };

    public static AdminOperationResult UserNotFound() => new()
    {
        Succeeded = false,
        NotFound = true,
        Message = "User not found."
    };

    public static AdminOperationResult BadRequest(
        string message,
        IEnumerable<string>? errors = null) => new()
        {
            Succeeded = false,
            Message = message,
            Errors = errors?.ToArray() ?? Array.Empty<string>()
        };

    public static AdminOperationResult InvalidRole(
        IEnumerable<string> allowedRoles) => new()
        {
            Succeeded = false,
            Message = "The requested role is invalid.",
            Errors = allowedRoles.ToArray()
        };

    /// <summary>
    /// A state conflict distinct from a bad request — e.g. "cannot extend a plan that was
    /// never selected." Mapped to 409 by AdminController.ToActionResult.
    /// </summary>
    public static AdminOperationResult ConflictResult(string message) => new()
    {
        Succeeded = false,
        Conflict = true,
        Message = message
    };
}

