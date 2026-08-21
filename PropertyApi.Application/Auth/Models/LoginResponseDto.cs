using System.Text.Json.Serialization;

namespace PropertyApi.Application.Auth.Models;

/// <summary>
/// Unified response model for login operations with comprehensive security information.
/// Ensures frontend can properly handle all authentication scenarios.
///
/// IMPORTANT: Program.cs sets JsonSerializerOptions.PropertyNamingPolicy = null, so named
/// DTOs serialize as PascalCase by default. The login contract has always been camelCase
/// (the previous endpoint returned an anonymous object with lowercase-written members), and
/// the Angular client reads camelCase only. Every member therefore carries an explicit
/// [JsonPropertyName] — do not remove them or the client silently stores empty tokens.
/// </summary>
public sealed class LoginResponseDto
{
    /// <summary>
    /// Whether login was successful.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// HTTP status code mirrored in the body (the transport status is authoritative).
    /// 200 = Success, 401 = Invalid credentials, 423 = Account locked, 429 = Rate limited.
    /// </summary>
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; init; }

    /// <summary>
    /// User-friendly message for display in UI.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Stable machine-readable error code — the discriminator the client should branch on.
    /// See <see cref="LoginErrorCodes"/>.
    /// </summary>
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Error category. Serialized as a string (JsonStringEnumConverter is registered globally),
    /// e.g. "InvalidCredentials" — not a number.
    /// </summary>
    [JsonPropertyName("errorType")]
    public LoginErrorType? ErrorType { get; init; }

    /// <summary>
    /// Access token on successful login.
    /// </summary>
    [JsonPropertyName("accessToken")]
    public string? AccessToken { get; init; }

    /// <summary>
    /// Refresh token on successful login.
    /// </summary>
    [JsonPropertyName("refreshToken")]
    public string? RefreshToken { get; init; }

    /// <summary>
    /// Access token lifetime in seconds.
    /// </summary>
    [JsonPropertyName("expiresIn")]
    public int ExpiresIn { get; init; }

    // ========== Security-Related Fields ==========

    /// <summary>
    /// Number of failed login attempts recorded for this account.
    /// </summary>
    [JsonPropertyName("failedAttemptCount")]
    public int FailedAttemptCount { get; init; }

    /// <summary>
    /// Maximum allowed failed attempts before lockout.
    /// </summary>
    [JsonPropertyName("maxFailedAttempts")]
    public int MaxFailedAttempts { get; init; } = 5;

    /// <summary>
    /// Remaining attempts before the account locks.
    /// </summary>
    [JsonPropertyName("remainingAttemptsBeforeLockout")]
    public int RemainingAttemptsBeforeLockout =>
        Math.Max(0, MaxFailedAttempts - FailedAttemptCount);

    /// <summary>
    /// Is the account currently locked?
    /// </summary>
    [JsonPropertyName("isAccountLocked")]
    public bool IsAccountLocked { get; init; }

    /// <summary>
    /// When the account unlocks (UTC). Null when not locked.
    /// </summary>
    [JsonPropertyName("lockoutEndTimeUtc")]
    public DateTime? LockoutEndTimeUtc { get; init; }

    /// <summary>
    /// Whole minutes remaining until unlock; 0 when not locked.
    /// Clients should prefer <see cref="LockoutEndTimeUtc"/> for countdowns, since this value
    /// is computed at serialization time and goes stale immediately.
    /// </summary>
    [JsonPropertyName("lockoutMinutesRemaining")]
    public int LockoutMinutesRemaining
    {
        get
        {
            if (!IsAccountLocked || !LockoutEndTimeUtc.HasValue)
                return 0;

            var remaining = (int)Math.Ceiling(
                (LockoutEndTimeUtc.Value - DateTime.UtcNow).TotalMinutes);
            return Math.Max(0, remaining);
        }
    }

    /// <summary>
    /// Localizable-by-the-client hint about remaining attempts.
    /// </summary>
    [JsonPropertyName("attemptWarningMessage")]
    public string? AttemptWarningMessage { get; init; }

    /// <summary>
    /// Password reset URL when the account must be secured.
    /// </summary>
    [JsonPropertyName("passwordResetUrl")]
    public string? PasswordResetUrl { get; init; }

    /// <summary>
    /// Action the client should steer the user toward.
    /// One of: "retry", "wait-and-retry", "change-password", "contact-support".
    /// </summary>
    [JsonPropertyName("recommendedAction")]
    public string? RecommendedAction { get; init; }

    // ========== Factory Methods ==========

    /// <summary>
    /// Creates a success response with tokens.
    /// </summary>
    public static LoginResponseDto CreateSuccess(
        string accessToken,
        string refreshToken,
        int expiresInSeconds)
    {
        return new LoginResponseDto
        {
            Success = true,
            StatusCode = 200,
            Message = "Login successful",
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = expiresInSeconds
        };
    }

    public static LoginResponseDto CreateInvalidCredentials(
        int failedAttemptCount = 0,
        int maxAttempts = 5)
    {
        var remainingAttempts = Math.Max(0, maxAttempts - failedAttemptCount);
        var warningMessage = remainingAttempts switch
        {
            0 => "Your account will lock after 1 more attempt",
            1 => "1 attempt remaining before lockout",
            _ => $"{remainingAttempts} attempts remaining before lockout"
        };

        return new LoginResponseDto
        {
            Success = false,
            StatusCode = 401,
            Message = "Invalid email or password",
            ErrorCode = LoginErrorCodes.InvalidCredentials,
            ErrorType = LoginErrorType.InvalidCredentials,
            FailedAttemptCount = failedAttemptCount,
            MaxFailedAttempts = maxAttempts,
            AttemptWarningMessage = warningMessage,
            RecommendedAction = "retry"
        };
    }

    public static LoginResponseDto CreateAccountLocked(
        DateTime lockoutEndUtc,
        int failedAttemptCount = 5)
    {
        var minutesRemaining = (int)(lockoutEndUtc - DateTime.UtcNow).TotalMinutes;
        minutesRemaining = Math.Max(0, minutesRemaining);

        return new LoginResponseDto
        {
            Success = false,
            StatusCode = 423,
            Message = minutesRemaining > 0
                ? $"Your account is locked. Please try again in {minutesRemaining} minute(s)."
                : "Your account is locked due to too many login attempts",
            ErrorCode = LoginErrorCodes.AccountLocked,
            ErrorType = LoginErrorType.AccountLocked,
            IsAccountLocked = true,
            LockoutEndTimeUtc = lockoutEndUtc,
            FailedAttemptCount = failedAttemptCount,
            MaxFailedAttempts = 5,
            AttemptWarningMessage = $"Account locked for {minutesRemaining} more minute(s)",
            RecommendedAction = "wait-and-retry"
        };
    }


    public static LoginResponseDto RateLimited(int retryAfterSeconds = 60)
    {
        return new LoginResponseDto
        {
            Success = false,
            StatusCode = 429,
            Message = $"Too many login attempts. Please try again in {retryAfterSeconds} second(s).",
            ErrorCode = LoginErrorCodes.RateLimited,
            ErrorType = LoginErrorType.RateLimited,
            RecommendedAction = "wait-and-retry"
        };
    }

    public static LoginResponseDto UserNotFound()
        => CreateInvalidCredentials();

    public static LoginResponseDto AccountDisabled()
    {
        return new LoginResponseDto
        {
            Success = false,
            StatusCode = 403,
            Message = "This account has been disabled",
            ErrorCode = LoginErrorCodes.AccountDisabled,
            ErrorType = LoginErrorType.AccountDisabled,
            RecommendedAction = "contact-support"
        };
    }

    public static LoginResponseDto PasswordChangeRequired(
        string passwordResetUrl,
        int failedAttemptCount)
    {
        return new LoginResponseDto
        {
            Success = false,
            StatusCode = 403,
            Message = "Your account requires immediate password change for security",
            ErrorCode = LoginErrorCodes.PasswordChangeRequired,
            ErrorType = LoginErrorType.PasswordChangeRequired,
            PasswordResetUrl = passwordResetUrl,
            FailedAttemptCount = failedAttemptCount,
            AttemptWarningMessage = "Unauthorized access attempts detected",
            RecommendedAction = "change-password"
        };
    }
}

/// <summary>
/// Enumeration of possible login error types for frontend error handling.
/// </summary>
public enum LoginErrorType
{
    /// <summary>
    /// Credentials are invalid (user or password incorrect).
    /// Frontend: Show "Invalid email or password" + attempts counter.
    /// </summary>
    InvalidCredentials = 1,

    /// <summary>
    /// Account is locked due to too many failed attempts.
    /// Frontend: Show lockout message with countdown.
    /// </summary>
    AccountLocked = 2,

    /// <summary>
    /// Rate limited - too many requests from same IP.
    /// Frontend: Show "too many attempts" message with retry timer.
    /// </summary>
    RateLimited = 3,

    /// <summary>
    /// Account has been disabled/deleted.
    /// Frontend: Show contact support message.
    /// </summary>
    AccountDisabled = 4,

    /// <summary>
    /// Password change required for security reasons.
    /// Frontend: Redirect to password change page with pre-filled reset token.
    /// </summary>
    PasswordChangeRequired = 5,

    /// <summary>
    /// Server error or other unexpected issue.
    /// Frontend: Show generic error message + contact support.
    /// </summary>
    ServerError = 6
}

/// <summary>
/// Stable error codes emitted on the login contract. These strings are part of the public API
/// and are mirrored in the Angular client (core/models/auth.model.ts → LOGIN_ERROR_CODES).
/// Changing a value is a breaking change.
/// </summary>
public static class LoginErrorCodes
{
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string RateLimited = "RATE_LIMITED";
    public const string AccountDisabled = "ACCOUNT_DISABLED";
    public const string PasswordChangeRequired = "PASSWORD_CHANGE_REQUIRED";
    public const string ServerError = "SERVER_ERROR";
}
