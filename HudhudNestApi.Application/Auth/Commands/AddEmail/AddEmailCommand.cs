using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Auth.DTOs;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Common.Security;

namespace HudhudNestApi.Application.Auth.Commands.AddEmail;

/// <summary>
/// Adds an email address to an existing identity account and sends
/// an email-confirmation token.
///
/// Application depends only on the framework-neutral Identity boundary.
/// </summary>
public sealed record AddEmailCommand(
    Guid UserId,
    string Email)
    : IRequest<AddEmailResult>;

public sealed class AddEmailCommandHandler
    : IRequestHandler<
        AddEmailCommand,
        AddEmailResult>
{
    private readonly IAddEmailIdentityService _identity;

    private readonly IEmailVerificationService _emailService;

    private readonly ILogger<AddEmailCommandHandler>
        _logger;

    public AddEmailCommandHandler(
        IAddEmailIdentityService identity,
        IEmailVerificationService emailService,
        ILogger<AddEmailCommandHandler> logger)
    {
        _identity = identity;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<AddEmailResult> Handle(
        AddEmailCommand request,
        CancellationToken ct)
    {
        var email =
            NormalizeEmail(
                request.Email);

        /*
         * 1. Resolve the current identity account.
         */
        var identity =
            await _identity.FindByIdAsync(
                request.UserId,
                ct);

        if (identity is null ||
            identity.IsDeleted)
        {
            return AddEmailResult.Fail(
                "USER_NOT_FOUND",
                "المستخدم غير موجود.");
        }

        /*
         * 2. Reject accounts that already have a confirmed email.
         */
        if (!string.IsNullOrWhiteSpace(
                identity.Email) &&
            identity.EmailConfirmed)
        {
            return AddEmailResult.Fail(
                "EMAIL_ALREADY_SET",
                "لديك بريد إلكتروني مفعّل بالفعل.");
        }

        /*
         * 3. Check whether another identity already owns the email.
         */
        var existingWithEmail =
            await _identity.FindByEmailAsync(
                email,
                ct);

        if (existingWithEmail is not null &&
            existingWithEmail.IdentityId !=
            identity.IdentityId)
        {
            return AddEmailResult.Fail(
                "EMAIL_TAKEN",
                "هذا البريد الإلكتروني مستخدم من قبل حساب آخر.");
        }

        /*
         * 4. Persist the unconfirmed email.
         */
        var setEmailResult =
            await _identity.SetEmailAsync(
                identity.IdentityId,
                email,
                ct);

        if (!setEmailResult.Succeeded)
        {
            _logger.LogError(
                "Failed to set email for identity {IdentityId}: {Errors}",
                identity.IdentityId,
                string.Join(
                    ", ",
                    setEmailResult.Errors));

            return AddEmailResult.Fail(
                "EMAIL_SET_FAILED",
                "فشل إضافة البريد.");
        }

        /*
         * 5. Generate a confirmation token and mail the link, letting no failure out.
         *
         * The email is already persisted on the account (step 4, committed). An
         * unconfirmed address is simply unconfirmed -- it can be resent later via the
         * dedicated resend endpoint. Rethrowing here would report a successful email
         * change as a failure while the new (unconfirmed) address is already saved,
         * which mirrors the same swallow-and-log policy RegisterCommandHandler and
         * ResendConfirmationEmailCommandHandler already use for this exact tradeoff.
         */
        try
        {
            var token =
                await _identity
                    .GenerateEmailConfirmationTokenAsync(
                        identity.IdentityId,
                        ct);

            await _emailService
                .SendVerificationLinkAsync(
                    email,
                    token,
                    ct);

            _logger.LogInformation(
                "Email verification sent to {Email} for identity {IdentityId}.",
                PiiMasking.MaskEmail(email),
                identity.IdentityId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Email {Email} was set for identity {IdentityId}, but the confirmation "
                + "email could not be sent. The address stays unconfirmed until it is resent.",
                PiiMasking.MaskEmail(email),
                identity.IdentityId);
        }

        return AddEmailResult.Ok();
    }

    private static string NormalizeEmail(
        string email)
        => email
            .Trim()
            .ToLowerInvariant();
}

public sealed class AddEmailCommandValidator
    : AbstractValidator<AddEmailCommand>
{
    public AddEmailCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage(
                "معرّف المستخدم مطلوب.");

        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(320)
            .EmailAddress()
            .Must(
                email =>
                    email != null &&
                    !email.Any(
                        char.IsWhiteSpace))
            .WithMessage(
                "Email must not contain whitespace.");
    }
}
