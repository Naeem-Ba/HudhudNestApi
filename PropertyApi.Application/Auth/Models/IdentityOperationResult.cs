namespace PropertyApi.Application.Auth.Models;

public sealed record IdentityOperationResult(
    bool Succeeded,
    IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Returned when account creation lost a race on the unique email index. Callers map
    /// it to a conflict rather than a validation failure.
    /// </summary>
    public const string DuplicateEmailCode = "DUPLICATE_EMAIL";

    public static IdentityOperationResult Success()
        => new(true, Array.Empty<string>());

    public static IdentityOperationResult Failed(IEnumerable<string> errors)
        => new(false, errors.ToArray());

    public static IdentityOperationResult Failed(params string[] errors)
        => new(false, errors);
}
