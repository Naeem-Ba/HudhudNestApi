namespace PropertyApi.Application.Auth.Models;

/// <summary>
/// Represents the result of password verification with lockout protection.
/// Distinct from Microsoft.AspNetCore.Identity.PasswordVerificationResult which
/// only indicates whether a password hash matches; this enum tracks lockout state.
/// </summary>
public enum LoginPasswordVerificationResult
{
    /// <summary>
    /// Password is incorrect; failed attempt count incremented.
    ///
    /// Deliberately value 0 so that default(LoginPasswordVerificationResult) denies access.
    /// With Success at 0 the type failed open: any stub, test double, or code path returning
    /// default would authenticate the caller.
    /// </summary>
    InvalidPassword = 0,

    /// <summary>Password is valid and account is not locked.</summary>
    Success = 1,

    /// <summary>Account is locked due to too many failed attempts.</summary>
    LockedOut = 2
}
