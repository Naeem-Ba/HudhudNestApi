using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Models;
using HudhudNestApi.Application.Common.Security;
using HudhudNestApi.Infrastructure.Email.Templates;
using HudhudNestApi.Infrastructure.Identity.Entities;

namespace HudhudNestApi.Infrastructure.Auth.Services;

public sealed class EmailVerificationService : IEmailVerificationService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationEmailSender _emailSender;
    private readonly IEmailConfirmationUrlBuilder _urlBuilder;
    private readonly IEmailConfirmationSendLimiter _sendLimiter;
    private readonly ILogger<EmailVerificationService> _logger;

    public EmailVerificationService(
        UserManager<ApplicationUser> userManager,
        IApplicationEmailSender emailSender,
        IEmailConfirmationUrlBuilder urlBuilder,
        IEmailConfirmationSendLimiter sendLimiter,
        ILogger<EmailVerificationService> logger)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _urlBuilder = urlBuilder;
        _sendLimiter = sendLimiter;
        _logger = logger;
    }

    public async Task SendVerificationLinkAsync(
        string email,
        string verificationToken,
        string? language,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));

        if (string.IsNullOrWhiteSpace(verificationToken))
            throw new ArgumentException("Verification token is required.", nameof(verificationToken));

        var user = await _userManager.FindByEmailAsync(email);

        if (user is null)
        {
            _logger.LogWarning(
                "Email verification requested for unknown email {Email}.",
                PiiMasking.MaskEmail(email));

            return;
        }

        // Every confirmation mail passes through here -- registration, add / resend from account
        // settings, the anonymous resend page -- so this is the one place a per-account cap cannot
        // be bypassed by picking a different door. Callers that must not reveal anything swallow the
        // exception; add-email reports it (see AddEmailCommandHandler).
        var retryAfter = await _sendLimiter.TryAcquireAsync(user.Id, ct);
        if (retryAfter is not null)
        {
            _logger.LogWarning(
                "Confirmation e-mail to {Email} not sent: identity {IdentityId} reached its limit.",
                PiiMasking.MaskEmail(email),
                user.Id);

            throw new EmailConfirmationLimitExceededException(retryAfter.Value);
        }

        var verificationUrl =
            _urlBuilder.Build(user.Id, verificationToken);

        await _emailSender.SendEmailAsync(
            new EmailMessage(
                email,
                EmailConfirmationTemplate.SubjectFor(language),
                EmailConfirmationTemplate.BuildHtml(verificationUrl, language),
                EmailConfirmationTemplate.BuildText(verificationUrl, language)),
            ct);
    }
}
