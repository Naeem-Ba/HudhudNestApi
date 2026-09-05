using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Security;

namespace PropertyApi.Infrastructure.Email;

/// <summary>
/// Implementation of ISecurityAlertService for sending security-related email notifications.
/// Handles failed login attempts, account lockouts, and password change requests.
/// </summary>
public sealed class SecurityAlertEmailService : ISecurityAlertService
{
    private readonly IEmailSender _emailSender;
    private readonly ILogger<SecurityAlertEmailService> _logger;

    public SecurityAlertEmailService(
        IEmailSender emailSender,
        ILogger<SecurityAlertEmailService> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
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

        var subject = failedAttemptCount switch
        {
            3 => "⚠️ Security Alert: 3 Failed Login Attempts to Your Account",
            4 => "🔴 Critical: 4 Failed Login Attempts - Your Account Will Lock Soon",
            _ => "Failed Login Attempt to Your Account"
        };

        var attemptInfo = $@"
Failed Login Attempt Details:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Attempt #{failedAttemptCount} at {attemptTimeUtc:F} (UTC)
From IP Address: {ipAddress ?? "Unknown"}
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━";

        var urgencyWarning = failedAttemptCount switch
        {
            3 => @"
⚠️ WARNING: Three failed attempts detected
If this wasn't you, your account is still accessible. Please change your password immediately.
",
            4 => @"
🔴 CRITICAL: Four failed attempts detected
Your account will be locked after one more failed attempt!
If this wasn't you, change your password RIGHT NOW to secure your account.
",
            _ => @"
ℹ️ This attempt was unsuccessful.
If this wasn't you, consider changing your password.
"
        };

        var body = $@"
Hello {userName},

{urgencyWarning}

{attemptInfo}

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
✅ IF YOU RECOGNIZE THIS: Ignore this email. No action needed.
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

❌ IF YOU DON'T RECOGNIZE THIS:
{(failedAttemptCount >= 3 ? "⏰ Change your password NOW before your account is locked!" : "")}

🔗 Secure Your Account:
• Change Password: [CHANGE_PASSWORD_LINK]
• Review Security: [SECURITY_DASHBOARD_LINK]
• Disable Access: [REVOKE_SESSIONS_LINK]

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
If you need help, contact our support team immediately.
This is an automated security alert - do not reply to this email.
";

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

        var subject = failedAttemptCount >= 4
            ? "🔴 URGENT: Change Your Password Immediately - Potential Unauthorized Access"
            : "🔒 Security Action Required: Update Your Password";

        var body = $@"
Hello {userName},

POTENTIAL UNAUTHORIZED ACCESS ATTEMPT DETECTED
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

Attack Details:
• Failed Attempts: {failedAttemptCount} attempts
• Last Attempt From: {ipAddress ?? "Unknown"}
• Account Status: {(failedAttemptCount >= 5 ? "🔴 LOCKED (15 minutes)" : "⚠️ AT RISK")}

IMMEDIATE ACTION REQUIRED:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

👉 Click the link below to CHANGE YOUR PASSWORD:
{passwordResetLink}

Link expires in: 24 hours

⚠️ IF YOU RECOGNIZE THESE ATTEMPTS:
You may have:
• Shared your password accidentally
• Reused password on another site that was breached
• Been targeted by automated attacks

ACTION PLAN:
1. ✅ Change your password to a strong, unique password
2. ✅ Never share your password with anyone
3. ✅ Enable two-factor authentication (if available)
4. ✅ Check active sessions and revoke suspicious ones

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

SECURITY TIPS:
• Use a password manager to generate strong passwords
• Enable 2FA for additional protection
• Review login activity regularly

Questions? Contact our security team: security@propertyapi.local

This is an automated security alert - do not reply to this email.
";

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

        const string subject = "🔴 CRITICAL: Your Account Has Been Locked";

        var body = $@"
Hello {userName},

YOUR ACCOUNT IS LOCKED
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

⛔ Too many failed login attempts detected

Lock Details:
• Reason: 5+ failed login attempts
• Locked From: {ipAddress ?? "Unknown"}
• Duration: {minutesRemaining} minutes
• Unlock Time: {(lockoutEnd.HasValue ? lockoutEnd.Value.ToString("F") + " UTC" : "Automatic")}

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
WHAT TO DO:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

✅ WAIT: Your account will automatically unlock in {minutesRemaining} minutes
   Then try logging in again with your correct password.

❌ DON'T RECOGNIZE THESE ATTEMPTS?
   Immediate actions:
   1. 🔑 Change your password: [PASSWORD_RESET_LINK]
   2. 🛡️ Secure your account: [SECURITY_SETTINGS_LINK]
   3. 📊 Review login history: [LOGIN_HISTORY_LINK]

⏰ ACCOUNT WILL UNLOCK AT: {lockoutEnd:F} UTC
   ({minutesRemaining} minutes from now)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

SECURITY CHECKLIST:
□ Change your password to something strong and unique
□ Verify no suspicious activity on your account
□ Check your computer for malware
□ Review and update recovery options (email, phone)
□ Enable two-factor authentication

STILL LOCKED AFTER {minutesRemaining} MINUTES?
Contact support: support@propertyapi.local

This is an automated security alert - do not reply to this email.
";

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

        const string subject = "✅ Password Changed Successfully";

        var body = $@"
Hello {userName},

YOUR PASSWORD HAS BEEN SUCCESSFULLY CHANGED
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

✅ Your account password was successfully updated on {DateTime.UtcNow:F} UTC

Your account is now secure with your new password.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
NEXT STEPS:
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

1. ✅ Log in with your new password
2. ✅ Review your active sessions
3. ✅ Verify all login locations are authorized

SECURITY RECOMMENDATION:
Consider enabling two-factor authentication for additional account protection.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

IF YOU DIDN'T CHANGE YOUR PASSWORD:
Your account may be compromised! Contact support immediately:
support@propertyapi.local

This is an automated notification - do not reply to this email.
";

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
