using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.DTOs;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Application.Auth.Commands.AddEmail;

// ══════════════════════════════════════════════════════════════
// إضافة البريد الإلكتروني لحساب موجود
//
// السيناريو:
// المستخدم سجّل بهاتفه فقط. بعد أسبوع يريد إضافة بريده
// الإلكتروني ليتمكن من الدخول بطريقتين.
//
// Clean Architecture:
// هذا الـ Handler لا يعتمد على UserManager مباشرة.
// يستخدم IIdentityUserService فقط، والتنفيذ الحقيقي في Infrastructure.
// ══════════════════════════════════════════════════════════════

public sealed record AddEmailCommand(
    Guid UserId,
    string Email
) : IRequest<AddEmailResult>;

public sealed class AddEmailCommandHandler
    : IRequestHandler<AddEmailCommand, AddEmailResult>
{
    private readonly IIdentityUserService _identityUsers;
    private readonly IEmailVerificationService _emailService;
    private readonly ILogger<AddEmailCommandHandler> _logger;

    public AddEmailCommandHandler(
        IIdentityUserService identityUsers,
        IEmailVerificationService emailService,
        ILogger<AddEmailCommandHandler> logger)
    {
        _identityUsers = identityUsers;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<AddEmailResult> Handle(
        AddEmailCommand request,
        CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);

        // ── 1. تحقق من وجود المستخدم ──────────────────────────
        var user = await _identityUsers.FindByIdAsync(request.UserId, ct);

        if (user is null || user.IsDeleted)
            return AddEmailResult.Fail("USER_NOT_FOUND", "المستخدم غير موجود.");

        // ── 2. هل البريد مُضاف بالفعل؟ ───────────────────────
        if (!string.IsNullOrWhiteSpace(user.Email) && user.EmailConfirmed)
            return AddEmailResult.Fail(
                "EMAIL_ALREADY_SET",
                "لديك بريد إلكتروني مُفعَّل بالفعل.");

        // ── 3. هل البريد مستخدم من قبل شخص آخر؟ ──────────────
        var existingWithEmail = await _identityUsers.FindByEmailAsync(email, ct);

        if (existingWithEmail is not null && existingWithEmail.Id != user.Id)
            return AddEmailResult.Fail(
                "EMAIL_TAKEN",
                "هذا البريد الإلكتروني مستخدم من قبل حساب آخر.");

        // ── 4. أضف البريد (غير مُفعَّل بعد) ───────────────────
        var setEmailResult = await _identityUsers.SetEmailAsync(user, email, ct);

        if (!setEmailResult.Succeeded)
        {
            _logger.LogError(
                "Failed to set email for user {UserId}: {Errors}",
                request.UserId,
                string.Join(", ", setEmailResult.Errors));

            return AddEmailResult.Fail("EMAIL_SET_FAILED", "فشل إضافة البريد.");
        }

        // ── 5. أنشئ رمز التحقق ────────────────────────────────
        var token = await _identityUsers.GenerateEmailConfirmationTokenAsync(user, ct);

        // ── 6. أرسل رسالة التحقق ──────────────────────────────
        await _emailService.SendVerificationLinkAsync(email, token, ct);

        _logger.LogInformation(
            "Email verification sent to {Email} for user {UserId}",
            email,
            request.UserId);

        return AddEmailResult.Ok();
    }

    private static string NormalizeEmail(string email)
        => email.Trim().ToLowerInvariant();
}

public sealed class AddEmailCommandValidator
    : AbstractValidator<AddEmailCommand>
{
    public AddEmailCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("معرّف المستخدم مطلوب.");

        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(320)
            .EmailAddress()
            .Must(email => email is not null && !email.Any(char.IsWhiteSpace))
            .WithMessage("Email must not contain whitespace.");
    }
}
