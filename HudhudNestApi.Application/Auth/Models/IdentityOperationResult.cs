namespace HudhudNestApi.Application.Auth.Models;

public sealed record IdentityOperationResult(
    bool Succeeded,
    IReadOnlyList<string> Errors)
{
    /// <summary>
    /// The stable code of each error, index for index with <see cref="Errors"/> (Identity's own
    /// "PasswordRequiresUpper", or a PASSWORD_* code from the password policy validator). Lets the API
    /// answer a rejected password with codes a localised client can translate instead of English prose.
    /// Empty when the producer has no codes.
    /// </summary>
    public IReadOnlyList<string> ErrorCodes { get; init; } = Array.Empty<string>();

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
