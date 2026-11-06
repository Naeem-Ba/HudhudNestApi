using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Common.Security;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Auth.Commands.ForgotPassword;

public sealed record ForgotPasswordCommand(
    string Email,
    string? RequestScheme,
    string? RequestHost,
    string? Language = null)
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

    private readonly ISecurityAlertLanguageResolver? _languageResolver;

    private readonly ILogger<ForgotPasswordCommandHandler>
        _logger;

    public ForgotPasswordCommandHandler(
        IForgotPasswordIdentityService identity,
        IUserAccountRepository userAccounts,
        IApplicationEmailSender emailSender,
        IPasswordResetUrlBuilder urlBuilder,
        ILogger<ForgotPasswordCommandHandler> logger,
        ISecurityAlertLanguageResolver? languageResolver = null)
    {
        _languageResolver = languageResolver;
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

            // The language the account holder chose wins (anyone can request a reset for someone
            // else's address, and the mail goes to the holder). Otherwise the site language the
            // request came from; otherwise Arabic (the default display language).
            var accountLanguage =
                _languageResolver is null
                    ? null
                    : await _languageResolver
                        .GetLanguageByEmailAsync(identity.Email, ct);

            var language =
                PasswordResetEmailTemplate.Resolve(
                    accountLanguage ?? request.Language);

            await _emailSender.SendEmailAsync(
                identity.Email,
                PasswordResetEmailTemplate.SubjectFor(language),
                PasswordResetEmailTemplate.BuildHtml(
                    language,
                    displayName,
                    resetUrl));

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

    private static string? BuildDisplayName(
        UserAccount? account)
    {
        if (account is null)
        {
            return null;
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
            ? null
            : name;
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
