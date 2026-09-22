namespace HudhudNestApi.Application.Auth.Interfaces;

/// <summary>
/// Hands a security alert to a background worker instead of sending it on the request
/// thread.
///
/// Login used to start the send and drop the task on the floor:
/// <c>_ = _securityAlerts.SendAccountLockedAlertAsync(...)</c>. ISecurityAlertService is
/// scoped and reaches a scoped DbContext, so that task raced ASP.NET Core disposing the
/// request scope -- and because nothing awaited it, the resulting ObjectDisposedException
/// was unobserved. The visible symptom was silence: an account-locked alert could simply
/// never arrive, with nothing in the logs saying so.
///
/// Enqueuing is synchronous, non-blocking, and never throws, so a login is never slowed
/// down or failed by the mail path. Delivery happens in SecurityAlertBackgroundService,
/// which creates its own scope per alert and logs every failure.
/// </summary>
public interface ISecurityAlertDispatcher
{
    void Enqueue(SecurityAlertRequest alert);
}

public enum SecurityAlertKind
{
    FailedLoginAttempt,
    AccountLocked,
    PasswordChangeRequest,
    PasswordChangedConfirmation
}

/// <summary>
/// A queued alert. Everything the send needs is captured by value at enqueue time, so
/// the worker never reaches back into the request's scope or its entities.
/// </summary>
public sealed record SecurityAlertRequest(
    SecurityAlertKind Kind,
    string UserEmail,
    string UserName,
    string? IpAddress = null,
    int FailedAttemptCount = 0,
    DateTime? AttemptTimeUtc = null,
    DateTime? LockoutEnd = null,
    string? PasswordResetLink = null);
