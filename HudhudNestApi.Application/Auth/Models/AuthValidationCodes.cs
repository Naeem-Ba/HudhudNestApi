namespace HudhudNestApi.Application.Auth.Models;

/// <summary>
/// Stable codes for the field-level validation failures of the sign-up forms (the 422 body's
/// <c>errorCodes</c>, index for index with <c>errors</c>). The Angular client translates each one and shows
/// it next to the field it belongs to. They are API surface: renaming one turns a translated message back
/// into the server's English sentence (pinned by AuthValidationCodesContractTests).
/// </summary>
public static class AuthValidationCodes
{
    public const string FirstNameRequired = "FIRST_NAME_REQUIRED";
    public const string FirstNameTooLong = "FIRST_NAME_TOO_LONG";
    public const string LastNameRequired = "LAST_NAME_REQUIRED";
    public const string LastNameTooLong = "LAST_NAME_TOO_LONG";
    public const string EmailRequired = "EMAIL_REQUIRED";
    public const string EmailTooLong = "EMAIL_TOO_LONG";
    public const string EmailInvalid = "EMAIL_INVALID";
    public const string EmailHasWhitespace = "EMAIL_HAS_WHITESPACE";
}
