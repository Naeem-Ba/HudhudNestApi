using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Security;

namespace PropertyApi.Application.Auth.Commands.ResendConfirmationEmail;

/// <summary>
/// Sends the address-confirmation link again, for the account that never received the
/// first one. Registration and add-email both mint a link once and never again; before
/// this, an unconfirmed account whose email went missing had no way forward.
/// </summary>
public sealed record ResendConfirmationEmailCommand(
    string Email)
    : IRequest<ResendConfirmationEmailResult>;

public sealed record ResendConfirmationEmailResult
{
    public bool Success { get; init; }

    public string Message { get; init; } =
        string.Empty;

    public static ResendConfirmationEmailResult Ok()
        => new()
        {
            Success = true,
            Message =
                "إذا كان البريد مسجّلاً وغير مؤكَّد، فقد أُرسل إليه رابط تأكيد جديد."
        };
}

public sealed class ResendConfirmationEmailCommandHandler
    : IRequestHandler<
        ResendConfirmationEmailCommand,
        ResendConfirmationEmailResult>
{
    private readonly IResendConfirmationIdentityService _identity;

    private readonly IEmailVerificationService _emailVerification;

    private readonly ILogger<ResendConfirmationEmailCommandHandler>
        _logger;

    public ResendConfirmationEmailCommandHandler(
        IResendConfirmationIdentityService identity,
        IEmailVerificationService emailVerification,
        ILogger<ResendConfirmationEmailCommandHandler> logger)
    {
        _identity = identity;
        _emailVerification = emailVerification;
        _logger = logger;
    }

    public async Task<ResendConfirmationEmailResult> Handle(
        ResendConfirmationEmailCommand request,
        CancellationToken ct)
    {
        var email =
            NormalizeEmail(request.Email);

        var identity =
            await _identity.FindByEmailAsync(
                email,
                ct);

        if (identity is null ||
            identity.IsDeleted ||
            string.IsNullOrWhiteSpace(identity.Email) ||
            identity.EmailConfirmed)
        {
            _logger.LogInformation(
                "Confirmation resend requested for {Email}; nothing to send.",
                PiiMasking.MaskEmail(email));

            return ResendConfirmationEmailResult.Ok();
        }

        try
        {
            var token =
                await _identity
                    .GenerateEmailConfirmationTokenAsync(
                        identity.IdentityId,
                        ct);

            await _emailVerification
                .SendVerificationLinkAsync(
                    identity.Email,
                    token,
                    ct);

            _logger.LogInformation(
                "Confirmation link resent for identity {IdentityId}.",
                identity.IdentityId);
        }
        catch (Exception ex)
        {
            // Swallowed for the same reason RegisterCommandHandler swallows it: letting a
            // provider outage turn into a 500 here would make the status code answer
            // "does this account exist?", which is the one question the identical
            // responses below exist to refuse.
            _logger.LogError(
                ex,
                "Confirmation link could not be resent for identity {IdentityId}.",
                identity.IdentityId);
        }

        // Always the same response, whether the address is unknown, already confirmed, or
        // was just sent to. Anything else enumerates accounts.
        return ResendConfirmationEmailResult.Ok();
    }

    private static string NormalizeEmail(
        string email)
        => email
            .Trim()
            .ToLowerInvariant();
}

public sealed class ResendConfirmationEmailCommandValidator
    : AbstractValidator<ResendConfirmationEmailCommand>
{
    public ResendConfirmationEmailCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(320)
            .EmailAddress();
    }
}
