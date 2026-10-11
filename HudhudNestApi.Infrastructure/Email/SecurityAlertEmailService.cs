using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Common.Security;

namespace HudhudNestApi.Infrastructure.Email;

/// <summary>
/// Implementation of ISecurityAlertService for sending security-related email notifications.
/// Handles failed login attempts, account lockouts, and password change requests.
/// Each email is written in the account owner's language (see <see cref="SecurityAlertTemplates"/>).
/// </summary>
public sealed class SecurityAlertEmailService : ISecurityAlertService
{
    private readonly IEmailSender _emailSender;
    private readonly ILogger<SecurityAlertEmailService> _logger;
    private readonly ISecurityAlertLanguageResolver? _languages;

    public SecurityAlertEmailService(
        IEmailSender emailSender,
        ILogger<SecurityAlertEmailService> logger,
        ISecurityAlertLanguageResolver? languages = null)
    {
        _emailSender = emailSender;
        _logger = logger;
        _languages = languages;
    }

    /// <summary>The account owner's language; English (the original wording) when unknown or on any lookup failure.</summary>
    private async Task<string> LanguageOfAsync(string userEmail, CancellationToken ct)
    {
        if (_languages is null)
        {
            return SecurityAlertTemplates.LegacyLanguage;
        }

        try
        {
            return SecurityAlertTemplates.Resolve(await _languages.GetLanguageByEmailAsync(userEmail, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Security alert language lookup failed; using English.");
            return SecurityAlertTemplates.LegacyLanguage;
        }
    }

    public async Task SendFailedLoginAttemptAlertAsync(
        string userEmail,
        string userName,
        int failedAttemptCount,
        string? ipAddress,
        DateTime attemptTimeUtc,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        var lang = await LanguageOfAsync(userEmail, ct);
        var (subject, body) = SecurityAlertTemplates.FailedLogin(lang, userName, failedAttemptCount, ipAddress, attemptTimeUtc);

        try
        {
            await _emailSender.SendEmailAsync(
                userEmail,
                subject,
                body);

            _logger.LogInformation(
                "Security alert email sent to {Email} for {FailedCount} failed attempt(s) from IP {IpAddress}",
                PiiMasking.MaskEmail(userEmail),
                failedAttemptCount,
                PiiMasking.MaskIp(ipAddress));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send security alert email to {Email}",
                PiiMasking.MaskEmail(userEmail));
            throw;
        }
    }

    public async Task SendPasswordChangeRequestAsync(
        string userEmail,
        string userName,
        string passwordResetLink,
        int failedAttemptCount,
        string? ipAddress,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordResetLink);

        var lang = await LanguageOfAsync(userEmail, ct);
        var (subject, body) = SecurityAlertTemplates.PasswordChangeRequest(lang, userName, passwordResetLink, failedAttemptCount, ipAddress);

        try
        {
            await _emailSender.SendEmailAsync(
                userEmail,
                subject,
                body);

            _logger.LogInformation(
                "Password change request email sent to {Email} after {FailedCount} failed attempts",
                PiiMasking.MaskEmail(userEmail),
                failedAttemptCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send password change request email to {Email}",
                PiiMasking.MaskEmail(userEmail));
            throw;
        }
    }

    public async Task SendAccountLockedAlertAsync(
        string userEmail,
        string userName,
        DateTime? lockoutEnd,
        string? ipAddress,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        var minutesRemaining = lockoutEnd.HasValue
            ? Math.Max(0, (int)(lockoutEnd.Value - DateTimeOffset.UtcNow).TotalMinutes)
            : 15;

        var lang = await LanguageOfAsync(userEmail, ct);
        var (subject, body) = SecurityAlertTemplates.AccountLocked(lang, userName, lockoutEnd, ipAddress, minutesRemaining);

        try
        {
            await _emailSender.SendEmailAsync(
                userEmail,
                subject,
                body);

            _logger.LogWarning(
                "Account locked notification sent to {Email} from IP {IpAddress}",
                PiiMasking.MaskEmail(userEmail),
                PiiMasking.MaskIp(ipAddress));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send account locked notification to {Email}",
                PiiMasking.MaskEmail(userEmail));
            throw;
        }
    }

    public async Task SendPasswordChangedConfirmationAsync(
        string userEmail,
        string userName,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        var lang = await LanguageOfAsync(userEmail, ct);
        var (subject, body) = SecurityAlertTemplates.PasswordChanged(lang, userName, DateTime.UtcNow);

        try
        {
            await _emailSender.SendEmailAsync(
                userEmail,
                subject,
                body);

            _logger.LogInformation(
                "Password change confirmation sent to {Email}",
                PiiMasking.MaskEmail(userEmail));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send password change confirmation to {Email}",
                PiiMasking.MaskEmail(userEmail));
        }
    }
}
