namespace HudhudNestApi.Application.Auth.Interfaces;

/// <summary>
/// Service for sending security alert emails about login failures and account lockouts.
/// </summary>
public interface ISecurityAlertService
{
    /// <summary>
    /// Sends a warning email after a failed login attempt.
    /// Used to alert users about unauthorized login attempts on their account.
    /// </summary>
    /// <param name="userEmail">Email address of the account owner</param>
    /// <param name="userName">Username/display name</param>
    /// <param name="failedAttemptCount">Current number of failed attempts</param>
    /// <param name="ipAddress">IP address from which the attempt was made</param>
    /// <param name="attemptTimeUtc">Time of the failed attempt (UTC)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task SendFailedLoginAttemptAlertAsync(
        string userEmail,
        string userName,
        int failedAttemptCount,
        string? ipAddress,
        DateTime attemptTimeUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Sends a critical alert requesting password change after multiple failed attempts.
    /// Includes secure password reset link.
    /// </summary>
    /// <param name="userEmail">Email address of the account owner</param>
    /// <param name="userName">Username/display name</param>
    /// <param name="passwordResetLink">URL-encoded password reset link</param>
    /// <param name="failedAttemptCount">Current number of failed attempts</param>
    /// <param name="ipAddress">IP address from which attempts were made</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task SendPasswordChangeRequestAsync(
        string userEmail,
        string userName,
        string passwordResetLink,
        int failedAttemptCount,
        string? ipAddress,
        CancellationToken ct = default);

    /// <summary>
    /// Sends notification that account has been locked due to too many failed login attempts.
    /// </summary>
    /// <param name="userEmail">Email address of the account owner</param>
    /// <param name="userName">Username/display name</param>
    /// <param name="lockoutEnd">When the lockout will expire (UTC)</param>
    /// <param name="ipAddress">IP address from which the last attempt was made</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task SendAccountLockedAlertAsync(
        string userEmail,
        string userName,
        DateTime? lockoutEnd,  // Can accept DateTime, DateTime?, or use DateTime.UtcNow-based values
        string? ipAddress,
        CancellationToken ct = default);

    /// <summary>
    /// Sends confirmation email after successful password change.
    /// </summary>
    /// <param name="userEmail">Email address of the account owner</param>
    /// <param name="userName">Username/display name</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Task representing the async operation</returns>
    Task SendPasswordChangedConfirmationAsync(
        string userEmail,
        string userName,
        CancellationToken ct = default);
}
