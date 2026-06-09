using FluentValidation;
using MediatR;
using PropertyApi.Domain.Users.Entities;
using Microsoft.AspNetCore.Identity;
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
// التسلسل:
// 1. المستخدم (مُسجَّل الدخول) يُرسل بريده الإلكتروني
// 2. نتحقق أن البريد غير مستخدم من قبل
// 3. نُرسل رابط تحقق لبريده
// 4. المستخدم ينقر الرابط → يُفعَّل البريد
// ══════════════════════════════════════════════════════════════

// ── الأمر ─────────────────────────────────────────────────────
public sealed record AddEmailCommand(
    Guid UserId,
    string Email
) : IRequest<AddEmailResult>;

// ── المعالج ───────────────────────────────────────────────────
public sealed class AddEmailCommandHandler
    : IRequestHandler<AddEmailCommand, AddEmailResult>
{
    private readonly UserManager<User> _userManager;
    private readonly IEmailVerificationService _emailService;
    private readonly ILogger<AddEmailCommandHandler> _logger;

    public AddEmailCommandHandler(
        UserManager<User> userManager,
        IEmailVerificationService emailService,
        ILogger<AddEmailCommandHandler> logger)
    {
        _userManager = userManager;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<AddEmailResult> Handle(
        AddEmailCommand request,
        CancellationToken ct)
    {
        // ── 1. تحقق من وجود المستخدم ──────────────────────────
        var user = await _userManager.FindByIdAsync(request.UserId.ToString());

        if (user is null)
            return AddEmailResult.Fail("USER_NOT_FOUND", "المستخدم غير موجود.");

        // ── 2. هل البريد مُضاف بالفعل؟ ───────────────────────
        if (!string.IsNullOrEmpty(user.Email) && user.EmailConfirmed)
            return AddEmailResult.Fail(
                "EMAIL_ALREADY_SET",
                "لديك بريد إلكتروني مُفعَّل بالفعل.");

        // ── 3. هل البريد مستخدم من قبل شخص آخر؟ ──────────────
        var existingWithEmail = await _userManager.FindByEmailAsync(request.Email);

        if (existingWithEmail is not null && existingWithEmail.Id != user.Id)
            return AddEmailResult.Fail(
                "EMAIL_TAKEN",
                "هذا البريد الإلكتروني مستخدم من قبل حساب آخر.");

        // ── 4. أضف البريد (غير مُفعَّل بعد) ───────────────────
        // SetEmailAsync يُعيّن البريد ويُعيّن EmailConfirmed = false تلقائياً
        var result = await _userManager.SetEmailAsync(user, request.Email);

        if (!result.Succeeded)
        {
            _logger.LogError(
                "Failed to set email for user {UserId}: {Errors}",
                request.UserId,
                string.Join(", ", result.Errors.Select(e => e.Description)));

            return AddEmailResult.Fail("EMAIL_SET_FAILED", "فشل إضافة البريد.");
        }

        // ── 5. أنشئ رمز التحقق ────────────────────────────────
        // GenerateEmailConfirmationTokenAsync = دالة من ASP.NET Identity
        // تُنشئ رمزاً آمناً مرتبطاً بالمستخدم
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

        // ── 6. أرسل رسالة التحقق ──────────────────────────────
        await _emailService.SendVerificationLinkAsync(
            request.Email,
            token,
            ct);

        _logger.LogInformation(
            "Email verification sent to {Email} for user {UserId}",
            request.Email, request.UserId);

        return AddEmailResult.Ok();
    }
}

// ── المُدقق ───────────────────────────────────────────────────
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
            .WithMessage("البريد الإلكتروني مطلوب.")
            .EmailAddress()
            .WithMessage("صيغة البريد الإلكتروني غير صحيحة.")
            .MaximumLength(320)
            .WithMessage("البريد الإلكتروني طويل جداً.");
    }
}
