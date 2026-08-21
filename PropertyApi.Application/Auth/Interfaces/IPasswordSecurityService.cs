namespace PropertyApi.Application.Auth.Interfaces;

/// <summary>
/// Validates password security and complexity requirements.
/// </summary>
public interface IPasswordSecurityService
{
    /// <summary>
    /// Validates password complexity requirements and checks against breach databases.
    /// </summary>
    /// <param name="password">The password to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with error messages if validation fails.</returns>
    Task<PasswordValidationResult> ValidatePasswordAsync(
        string password,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of password validation.
/// </summary>
public sealed record PasswordValidationResult
{
    /// <summary>
    /// Gets a value indicating whether the password passed all validations.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets the list of validation error messages.
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; }
        = Array.Empty<string>();

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static PasswordValidationResult Success()
        => new() { IsValid = true };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static PasswordValidationResult Failure(params string[] errors)
        => new()
        {
            IsValid = false,
            Errors = errors
        };

    /// <summary>
    /// Creates a failed validation result with multiple errors.
    /// </summary>
    public static PasswordValidationResult Failure(IEnumerable<string> errors)
        => new()
        {
            IsValid = false,
            Errors = errors.ToList().AsReadOnly()
        };
}
