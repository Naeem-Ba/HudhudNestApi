using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Infrastructure.Auth.Services;

public sealed class EmailVerificationService : IEmailVerificationService
{
    private readonly UserManager<User> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailVerificationService> _logger;

    public EmailVerificationService(
        UserManager<User> userManager,
        IEmailSender emailSender,
        IConfiguration configuration,
        ILogger<EmailVerificationService> logger)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendVerificationLinkAsync(
        string email,
        string token,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));

        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Verification token is required.", nameof(token));

        var user = await _userManager.FindByEmailAsync(email);

        if (user is null)
        {
            _logger.LogWarning(
                "Email verification requested for unknown email {Email}.",
                email);

            return;
        }

        var frontendBaseUrl =
            _configuration["Frontend:BaseUrl"] ??
            _configuration["App:FrontendBaseUrl"] ??
            "http://localhost:4200";

        var encodedEmail = WebUtility.UrlEncode(email);
        var encodedToken = WebUtility.UrlEncode(token);

        var verificationUrl =
            $"{frontendBaseUrl.TrimEnd('/')}/auth/verify-email" +
            $"?userId={user.Id}&email={encodedEmail}&token={encodedToken}";

        var htmlBody = $"""
            <p>Hello,</p>
            <p>Please confirm your email address by clicking the link below:</p>
            <p><a href="{verificationUrl}">Confirm email</a></p>
            <p>If you did not request this, you can ignore this email.</p>
            """;

        await _emailSender.SendEmailAsync(
            email,
            "Confirm your email",
            htmlBody);
    }
}