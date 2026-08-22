using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Abstractions;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Application.Auth.Commands.Login;

public sealed record LoginCommand(
    string Email,
    string Password,
    string? IpAddress = null) : IRequest<LoginResult>;

public sealed record LoginResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public int ExpiresIn { get; init; }

    public static LoginResult Ok(
        string accessToken,
        string refreshToken,
        int expiresIn) =>
        new()
        {
            Success = true,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresIn = expiresIn
        };

    public static LoginResult InvalidCredentials() =>
        new() { Message = "Invalid credentials." };
}

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, LoginResult>
{
    private readonly ILoginIdentityService _identity;
    private readonly IAuthenticationSessionIssuer _sessions;
    private readonly ILogger<LoginCommandHandler> _logger;
    private readonly ISecurityAlertDispatcher _securityAlerts;

    // Takes the dispatcher, not ISecurityAlertService. Sending an email from here meant
    // either awaiting SMTP inside the login request or discarding the task and racing
    // scope disposal; the queue removes that choice.
    public LoginCommandHandler(
        ILoginIdentityService identity,
        IAuthenticationSessionIssuer sessions,
        ILogger<LoginCommandHandler> logger,
        ISecurityAlertDispatcher securityAlerts)
    {
        _identity = identity;
        _sessions = sessions;
        _logger = logger;
        _securityAlerts = securityAlerts;
    }

    public async Task<LoginResult> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var identity = await _identity.FindByEmailAsync(email, cancellationToken);
        if (identity is null || identity.IsDeleted)
        {
            // Spend the same hashing time a real verification would, so "no such
            // account" cannot be told from "wrong password" by response time alone.
            // Returning here without hashing was a free user-enumeration oracle.
            await _identity.VerifyDummyPasswordAsync(cancellationToken);

            _logger.LogWarning("Login failed: no active account for the submitted address.");
            return LoginResult.InvalidCredentials();
        }

        // Check if account is locked due to too many failed attempts
        if (await _identity.IsLockedOutAsync(identity.IdentityId, cancellationToken))
        {
            var lockoutEnd = await _identity.GetLockoutEndAsync(identity.IdentityId, cancellationToken);
            var failedCount = await _identity.GetAccessFailedCountAsync(identity.IdentityId, cancellationToken);
            _logger.LogWarning(
                "Account is locked. UserId: {UserId}, FailedAttempts: {FailedCount}, LockoutUntil: {LockoutEnd}",
                identity.IdentityId,
                failedCount,
                lockoutEnd);

            // A locked account also returned before hashing anything, which made it the
            // fastest of the three outcomes and therefore identifiable on its own.
            await _identity.VerifyDummyPasswordAsync(cancellationToken);

            // Hand the alert to the background dispatcher. This used to be
            // `_ = SendAccountLockedAlertAsync(...)`, a discarded task holding a scoped
            // ISecurityAlertService and its DbContext past the end of the request scope.
            QueueAccountLockedAlert(
                identity,
                lockoutEnd?.DateTime,
                request.IpAddress);

            return LoginResult.InvalidCredentials();
        }

        // Verify password with lockout tracking
        var passwordVerificationResult = await _identity.VerifyPasswordWithLockoutAsync(
            identity.IdentityId,
            request.Password,
            cancellationToken);

        if (passwordVerificationResult != LoginPasswordVerificationResult.Success)
        {
            var failedCount = await _identity.GetAccessFailedCountAsync(identity.IdentityId, cancellationToken);
            _logger.LogWarning(
                "Password authentication failed. UserId: {UserId}, FailedAttempts: {FailedCount}, Result: {VerificationResult}",
                identity.IdentityId,
                failedCount,
                passwordVerificationResult);

            // Same change as the lockout path: queue rather than discard a running task.
            QueueFailedLoginAlert(
                identity,
                failedCount,
                request.IpAddress);

            return LoginResult.InvalidCredentials();
        }

        var session = await _sessions.IssueAsync(
            new AuthenticationSessionRequest(
                identity,
                "password",
                request.IpAddress,
                SuccessfulLoginRecordingMode.BestEffort),
            cancellationToken);
        return session.Succeeded
            ? LoginResult.Ok(
                session.AccessToken!,
                session.RefreshToken!,
                session.ExpiresInSeconds)
            : LoginResult.InvalidCredentials();
    }

    /// <summary>
    /// Queues the "your account is locked" alert, when there is an address to send it to.
    /// </summary>
    private void QueueAccountLockedAlert(
        IdentityAccountSnapshot user,
        DateTime? lockoutEnd,
        string? ipAddress)
    {
        // Phone-only accounts exist and have nowhere to deliver an email alert.
        if (string.IsNullOrWhiteSpace(user.Email) ||
            string.IsNullOrWhiteSpace(user.UserName))
        {
            return;
        }

        _securityAlerts.Enqueue(new SecurityAlertRequest(
            SecurityAlertKind.AccountLocked,
            UserEmail: user.Email,
            UserName: user.UserName,
            IpAddress: ipAddress,
            LockoutEnd: lockoutEnd));
    }

    /// <summary>
    /// Queues the appropriate security alert based on failed attempt count.
    /// Alert levels:
    /// - 3 attempts: Warning email
    /// - 4 attempts: Critical warning with password change request
    /// </summary>
    private void QueueFailedLoginAlert(
        IdentityAccountSnapshot user,
        int failedAttemptCount,
        string? ipAddress)
    {
        if (failedAttemptCount is not (3 or 4) ||
            string.IsNullOrWhiteSpace(user.Email) ||
            string.IsNullOrWhiteSpace(user.UserName))
        {
            return;
        }

        _securityAlerts.Enqueue(new SecurityAlertRequest(
            SecurityAlertKind.FailedLoginAttempt,
            UserEmail: user.Email,
            UserName: user.UserName,
            IpAddress: ipAddress,
            FailedAttemptCount: failedAttemptCount,
            AttemptTimeUtc: DateTime.UtcNow));
    }
}

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(320).EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
