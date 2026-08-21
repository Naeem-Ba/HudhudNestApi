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
    private readonly ISecurityAlertService _securityAlerts;

    public LoginCommandHandler(
        ILoginIdentityService identity,
        IAuthenticationSessionIssuer sessions,
        ILogger<LoginCommandHandler> logger,
        ISecurityAlertService securityAlerts)
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
            _logger.LogWarning("User not found or deleted for email: {Email}", email);
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

            // Send account locked notification email (best effort - don't fail login if email fails)
            _ = _securityAlerts.SendAccountLockedAlertAsync(
                identity.Email,
                identity.UserName,
                lockoutEnd.HasValue ? lockoutEnd.Value.DateTime : (DateTime?)null,
                request.IpAddress,
                cancellationToken).ConfigureAwait(false);

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

            // Send security alert emails based on failed attempt count
            // Best effort - don't fail login if email sending fails
            _ = SendSecurityAlertEmailAsync(
                identity,
                failedCount,
                request.IpAddress,
                cancellationToken);

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
    /// Sends appropriate security alert emails based on failed attempt count.
    /// Alert levels:
    /// - 3 attempts: Warning email
    /// - 4 attempts: Critical warning with password change request
    /// </summary>
    private async Task SendSecurityAlertEmailAsync(
        IdentityAccountSnapshot user,
        int failedAttemptCount,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        try
        {
            if (failedAttemptCount is 3 or 4 &&
                !string.IsNullOrWhiteSpace(user.Email) &&
                !string.IsNullOrWhiteSpace(user.UserName))
            {
                // Send warning/critical alert
                await _securityAlerts.SendFailedLoginAttemptAlertAsync(
                    user.Email!,
                    user.UserName!,
                    failedAttemptCount,
                    ipAddress,
                    DateTime.UtcNow,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            // Log but don't throw - email is best effort
            _logger.LogError(
                ex,
                "Failed to send security alert email for user {UserId} with {FailedCount} failed attempts",
                user.IdentityId,
                failedAttemptCount);
        }
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
