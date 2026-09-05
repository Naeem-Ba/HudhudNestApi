using System.Net;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Security;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Auth.Commands.ForgotPassword;

public sealed record ForgotPasswordCommand(
    string Email,
    string? RequestScheme,
    string? RequestHost)
    : IRequest<ForgotPasswordResult>;

public sealed record ForgotPasswordResult
{
    public bool Success { get; init; }

    public string Message { get; init; } =
        string.Empty;

    public static ForgotPasswordResult Ok()
        => new()
        {
            Success = true,
            Message =
                "If the email is registered, a password reset link has been sent."
        };

    public static ForgotPasswordResult BadRequest(
        string message)
        => new()
        {
            Success = false,
            Message = message
        };
}

public sealed class ForgotPasswordCommandHandler
    : IRequestHandler<
        ForgotPasswordCommand,
        ForgotPasswordResult>
{
    private readonly IForgotPasswordIdentityService _identity;

    private readonly IUserAccountRepository _userAccounts;

    private readonly IApplicationEmailSender _emailSender;

    private readonly IPasswordResetUrlBuilder _urlBuilder;

    private readonly ILogger<ForgotPasswordCommandHandler>
        _logger;

    public ForgotPasswordCommandHandler(
        IForgotPasswordIdentityService identity,
        IUserAccountRepository userAccounts,
        IApplicationEmailSender emailSender,
        IPasswordResetUrlBuilder urlBuilder,
        ILogger<ForgotPasswordCommandHandler> logger)
    {
        _identity = identity;
        _userAccounts = userAccounts;
        _emailSender = emailSender;
        _urlBuilder = urlBuilder;
        _logger = logger;
    }

    public async Task<ForgotPasswordResult> Handle(
        ForgotPasswordCommand request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(
                request.Email))
        {
            return ForgotPasswordResult.BadRequest(
                "Email is required.");
        }

        var email =
            NormalizeEmail(request.Email);

        var identity =
            await _identity.FindByEmailAsync(
                email,
                ct);

        if (identity is not null &&
            !identity.IsDeleted &&
            !string.IsNullOrWhiteSpace(
                identity.Email))
        {
            var token =
                await _identity
                    .GeneratePasswordResetTokenAsync(
                        identity.IdentityId,
                        ct);

            var resetUrl =
                _urlBuilder.Build(
                    email,
                    token,
                    request.RequestScheme,
                    request.RequestHost);

            var account =
                await _userAccounts.GetByIdAsync(
                    identity.UserAccountId,
                    ct);

            var displayName =
                BuildDisplayName(account);

            var emailBody =
                BuildPasswordResetEmailBody(
                    displayName,
                    resetUrl);

            await _emailSender.SendEmailAsync(
                identity.Email,
                "Reset your password",
                emailBody);

            _logger.LogInformation(
                "Password reset email sent for identity {IdentityId}.",
                identity.IdentityId);
        }
        else
        {
            _logger.LogInformation(
                "Password reset requested for non-existing or deleted email {Email}.",
                PiiMasking.MaskEmail(email));
        }

        // Always return the same response to prevent account enumeration.
        return ForgotPasswordResult.Ok();
    }

    private static string BuildDisplayName(
        UserAccount? account)
    {
        if (account is null)
        {
            return "there";
        }

        if (!string.IsNullOrWhiteSpace(
                account.DisplayName))
        {
            return account.DisplayName;
        }

        var name =
            $"{account.FirstName} {account.LastName}"
                .Trim();

        return string.IsNullOrWhiteSpace(name)
            ? "there"
            : name;
    }

    private static string BuildPasswordResetEmailBody(
        string displayName,
        string resetUrl)
    {
        return $"""
            <html>
            <body style="font-family:Arial,sans-serif;line-height:1.6">
                <p>Hello {WebUtility.HtmlEncode(displayName)},</p>
                <p>We received a request to reset your PropertyApi password.</p>
                <p><a href="{WebUtility.HtmlEncode(resetUrl)}">Reset your password</a></p>
                <p>If you did not request this, you can safely ignore this email.</p>
            </body>
            </html>
            """;
    }

    private static string NormalizeEmail(
        string email)
        => email
            .Trim()
            .ToLowerInvariant();
}

public sealed class ForgotPasswordCommandValidator
    : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(320)
            .EmailAddress();
    }
}
