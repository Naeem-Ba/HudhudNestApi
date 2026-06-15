// ================================================================
// الموقع: PropertyApi.Application/Auth/Commands/VerifyEmail/VerifyEmailCommand.cs
// الـ Namespace: PropertyApi.Application.Auth.Commands.VerifyEmail
//
// الغرض: تفعيل البريد الإلكتروني عبر الرمز المُرسَل بالبريد
//
// تسلسل الاستخدام:
//   1. المستخدم يستدعي AddEmailCommand → يتلقى بريداً بالرابط
//   2. يضغط الرابط → Frontend يُرسل POST /api/auth/email/verify
//   3. VerifyEmailCommand يُنفَّذ → EmailConfirmed = true
// ================================================================
using FluentValidation;
using MediatR;

namespace PropertyApi.Application.Auth.Commands.VerifyEmail;

// ═══════════════════════════════════════════════════════════════
// 1. نتيجة الأمر
// ═══════════════════════════════════════════════════════════════
public sealed record VerifyEmailResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static VerifyEmailResult Ok() =>
        new() { Success = true };

    public static VerifyEmailResult Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, ErrorMessage = message };
}

// ═══════════════════════════════════════════════════════════════
// 2. الأمر (Command)
// ═══════════════════════════════════════════════════════════════
/// <summary>
/// أمر تفعيل البريد الإلكتروني
/// ────────────────────────────
/// يُستدعى عندما ينقر المستخدم رابط التحقق في بريده.
///
/// مثال على الطلب:
/// POST /api/auth/email/verify
/// {
///   "userId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
///   "token":  "CfDJ8Lq5m..."  ← رمز ASP.NET Identity (مُشفَّر)
/// }
/// </summary>
public sealed record VerifyEmailCommand(
    Guid UserId,

    /// <summary>
    /// الرمز المُنشَأ بواسطة:
    /// the identity provider generates an email confirmation token for the user
    /// يجب URL-Decode قبل الاستخدام (لأنه يحتوي أحرف خاصة)
    /// </summary>
    string Token
) : IRequest<VerifyEmailResult>;

// ═══════════════════════════════════════════════════════════════
// 3. المُدقق (Validator)
// ═══════════════════════════════════════════════════════════════
/// <summary>
/// يتحقق من صحة الحقول قبل تنفيذ المعالج.
/// يعمل تلقائياً عبر ValidationBehavior في MediatR Pipeline.
/// </summary>
public sealed class VerifyEmailCommandValidator
    : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty()
            .WithMessage("معرّف المستخدم مطلوب.")
            .Must(id => id != Guid.Empty)
            .WithMessage("معرّف المستخدم غير صالح.");

        RuleFor(x => x.Token)
            .NotEmpty()
            .WithMessage("رمز التحقق مطلوب.")
            .MinimumLength(10)
            .WithMessage("رمز التحقق غير صالح.");
    }
}
