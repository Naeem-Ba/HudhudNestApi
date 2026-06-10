using System.Net;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Auth.Commands.ForgotPassword;

public sealed record ForgotPasswordCommand(
    string Email,
    string? RequestScheme,
    string? RequestHost) : IRequest<ForgotPasswordResult>;

public sealed record ForgotPasswordResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;

    public static ForgotPasswordResult Ok() => new()
    {
        Success = true,
        Message = "If the email is registered, a password reset link has been sent."
    };

    public static ForgotPasswordResult BadRequest(string message) => new()
    {
        Success = false,
        Message = message
    };
}

public sealed class ForgotPasswordCommandHandler
    : IRequestHandler<ForgotPasswordCommand, ForgotPasswordResult>
{
    private readonly IIdentityUserService _identityUsers;
    private readonly IApplicationEmailSender _emailSender;
    private readonly IPasswordResetUrlBuilder _urlBuilder;
    private readonly ILogger<ForgotPasswordCommandHandler> _logger;

    public ForgotPasswordCommandHandler(
        IIdentityUserService identityUsers,
        IApplicationEmailSender emailSender,
        IPasswordResetUrlBuilder urlBuilder,
        ILogger<ForgotPasswordCommandHandler> logger)
    {
        _identityUsers = identityUsers;
        _emailSender = emailSender;
        _urlBuilder = urlBuilder;
        _logger = logger;
    }

    public async Task<ForgotPasswordResult> Handle(
        ForgotPasswordCommand request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return ForgotPasswordResult.BadRequest("Email is required.");

        var email = NormalizeEmail(request.Email);
        var user = await _identityUsers.FindByEmailAsync(email, ct);

        if (user is not null && !user.IsDeleted && !string.IsNullOrWhiteSpace(user.Email))
        {
            var token = await _identityUsers.GeneratePasswordResetTokenAsync(user, ct);

            var resetUrl = _urlBuilder.Build(
                email,
                token,
                request.RequestScheme,
                request.RequestHost);

            var emailBody = BuildPasswordResetEmailBody(user, resetUrl);

            await _emailSender.SendEmailAsync(
                user.Email!,
                "Reset your password",
                emailBody);

            _logger.LogInformation(
                "Password reset email sent for user {UserId}.",
                user.Id);
        }
        else
        {
            _logger.LogInformation(
                "Password reset requested for non-existing or deleted email {Email}.",
                email);
        }

        // Always return the same response to prevent account enumeration.
        return ForgotPasswordResult.Ok();
    }

    private static string BuildPasswordResetEmailBody(User user, string resetUrl)
    {
        var name = !string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.DisplayName
            : $"{user.FirstName} {user.LastName}".Trim();

        if (string.IsNullOrWhiteSpace(name))
            name = "there";

        return $"""
            <html>
            <body style="font-family:Arial,sans-serif;line-height:1.6">
                <p>Hello {WebUtility.HtmlEncode(name)},</p>
                <p>We received a request to reset your PropertyApi password.</p>
                <p><a href="{WebUtility.HtmlEncode(resetUrl)}">Reset your password</a></p>
                <p>If you did not request this, you can safely ignore this email.</p>
            </body>
            </html>
            """;
    }

    private static string NormalizeEmail(string email)
        => email.Trim().ToLowerInvariant();
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
