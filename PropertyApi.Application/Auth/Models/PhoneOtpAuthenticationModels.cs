using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Auth.Models;

public enum OtpValidationKind
{
    Valid,
    NotFound,
    Invalid,
    Expired,
    AlreadyConsumed,
    AttemptLimitExceeded,
    WrongCode
}

public sealed record OtpValidationResult(
    OtpValidationKind Kind,
    Guid? OtpCodeId = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

public sealed record ResolvedPhoneAccount(
    IdentityAccountSnapshot? Identity,
    UserAccount? Account,
    bool IsNewUser,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static ResolvedPhoneAccount Failed(
        string errorCode = "USER_CREATE_FAILED",
        string errorMessage = "Could not create or initialize the account.") =>
        new(null, null, false, errorCode, errorMessage);
}
