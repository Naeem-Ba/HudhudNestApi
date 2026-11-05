namespace HudhudNestApi.Application.Auth.Interfaces;

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

    /// <summary>
    /// Same checks, plus the rule that the password must not contain the account holder's own
    /// name, the name part of their e-mail address or a run of their phone number. Pass only
    /// what is reliably known; a null member is simply not checked.
    /// </summary>
    Task<PasswordValidationResult> ValidatePasswordAsync(
        string password,
        PasswordCheckContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The personal details a password is compared against (see <c>PasswordPolicy</c>). There is no date of
/// birth in the account model, so none is carried here.
/// </summary>
public sealed record PasswordCheckContext(
    string? FirstName = null,
    string? LastName = null,
    string? Email = null,
    string? PhoneNumber = null,
    string? UserName = null)
{
    public static PasswordCheckContext None { get; } = new();
}

/// <summary>
/// The stable identifier for every way a password can be rejected.
///
/// These exist because the English sentence beside them is not a user-facing string in
/// a trilingual product: the Angular client serves ar/en/de, and until these codes were
/// added it had nothing to key a translation off, so it printed the server's English
/// prose to Arabic and German users verbatim. The code is the contract; the message is
/// a developer-facing default and a fallback for clients that do not know the code.
///
/// Treat them as API surface -- renaming one silently reverts a translated message back
/// to English. PasswordPolicyContractTests pins the set.
/// </summary>
public static class PasswordErrorCodes
{
    public const string Required = "PASSWORD_REQUIRED";
    public const string TooShort = "PASSWORD_TOO_SHORT";
    public const string NoUppercase = "PASSWORD_NO_UPPERCASE";
    public const string NoLowercase = "PASSWORD_NO_LOWERCASE";
    public const string NoDigit = "PASSWORD_NO_DIGIT";
    public const string NoSpecialCharacter = "PASSWORD_NO_SPECIAL";
    public const string CommonWord = "PASSWORD_COMMON_WORD";
    public const string LongRun = "PASSWORD_LONG_RUN";
    public const string Breached = "PASSWORD_BREACHED";

    /// <summary>The password contains the holder's name, e-mail name part or phone digits.</summary>
    public const string PersonalInfo = "PASSWORD_CONTAINS_PERSONAL_INFO";
}

/// <summary>
/// One reason a password was rejected: a stable code for clients to translate, and an
/// English message for logs and for clients that do not recognise the code.
/// </summary>
/// <param name="Code">One of <see cref="PasswordErrorCodes"/>.</param>
/// <param name="Message">Developer-facing English description of the same failure.</param>
public sealed record PasswordValidationError(string Code, string Message);

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
    /// Gets the list of validation errors, each carrying a stable code and a message.
    /// </summary>
    public IReadOnlyList<PasswordValidationError> Errors { get; init; }
        = Array.Empty<PasswordValidationError>();

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static PasswordValidationResult Success()
        => new() { IsValid = true };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static PasswordValidationResult Failure(params PasswordValidationError[] errors)
        => new()
        {
            IsValid = false,
            Errors = errors
        };

    /// <summary>
    /// Creates a failed validation result with multiple errors.
    /// </summary>
    public static PasswordValidationResult Failure(IEnumerable<PasswordValidationError> errors)
        => new()
        {
            IsValid = false,
            Errors = errors.ToList().AsReadOnly()
        };
}
