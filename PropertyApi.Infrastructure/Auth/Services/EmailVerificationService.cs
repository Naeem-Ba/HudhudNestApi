using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Models;
using PropertyApi.Application.Common.Security;
using PropertyApi.Infrastructure.Email.Templates;
using PropertyApi.Infrastructure.Identity.Entities;

namespace PropertyApi.Infrastructure.Auth.Services;

public sealed class EmailVerificationService : IEmailVerificationService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationEmailSender _emailSender;
    private readonly IEmailConfirmationUrlBuilder _urlBuilder;
    private readonly ILogger<EmailVerificationService> _logger;

    public EmailVerificationService(
        UserManager<ApplicationUser> userManager,
        IApplicationEmailSender emailSender,
        IEmailConfirmationUrlBuilder urlBuilder,
        ILogger<EmailVerificationService> logger)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _urlBuilder = urlBuilder;
        _logger = logger;
    }

    public async Task SendVerificationLinkAsync(
        string email,
        string verificationToken,
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

        var verificationUrl =
            _urlBuilder.Build(user.Id, verificationToken);

        await _emailSender.SendEmailAsync(
            new EmailMessage(
                email,
                EmailConfirmationTemplate.Subject,
                EmailConfirmationTemplate.BuildHtml(verificationUrl),
                EmailConfirmationTemplate.BuildText(verificationUrl)),
            ct);
    }
}
