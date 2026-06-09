using MediatR;
using Microsoft.AspNetCore.Identity;
using PropertyApi.Domain.Users.Entities;
using Microsoft.Extensions.Logging;

namespace PropertyApi.Application.Auth.Commands.VerifyEmail;

/// <summary>
/// معالج تفعيل البريد الإلكتروني
/// ─────────────────────────────
/// خطوات التنفيذ:
/// 1. ابحث عن المستخدم بـ UserId
/// 2. تحقق أن البريد موجود وغير مُفعَّل
/// 3. استدعِ UserManager.ConfirmEmailAsync (يتحقق من الرمز تلقائياً)
/// 4. إذا نجح → EmailConfirmed = true في قاعدة البيانات
///
/// لماذا UserManager.ConfirmEmailAsync وليس تحقق يدوي؟
/// ───────────────────────────────────────────────────
/// ConfirmEmailAsync يتحقق من الرمز باستخدام SecurityStamp الخاص
/// بالمستخدم. إذا تغير SecurityStamp (مثلاً: تغيير كلمة المرور)،
/// الرمز القديم يُبطَل تلقائياً — وهذا سلوك أمني صحيح.
/// </summary>
public sealed class VerifyEmailCommandHandler
    : IRequestHandler<VerifyEmailCommand, VerifyEmailResult>
{
    private readonly UserManager<User> _userManager;
    private readonly ILogger<VerifyEmailCommandHandler> _logger;

    public VerifyEmailCommandHandler(
        UserManager<User> userManager,
        ILogger<VerifyEmailCommandHandler> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<VerifyEmailResult> Handle(
        VerifyEmailCommand command, CancellationToken ct)
    {
        // ── 1. ابحث عن المستخدم ───────────────────────────────
        var user = await _userManager.FindByIdAsync(
            command.UserId.ToString());

        if (user is null || user.IsDeleted)
        {
            _logger.LogWarning(
                "VerifyEmail: User not found [{UserId}]",
                command.UserId);

            // نُعيد رسالة عامة لعدم كشف وجود المستخدم من عدمه
            return VerifyEmailResult.Fail(
                "INVALID_TOKEN",
                "رابط التحقق غير صالح أو منتهي الصلاحية.");
        }

        // ── 2. هل البريد مُفعَّل بالفعل؟ ─────────────────────
        if (user.EmailConfirmed)
        {
            _logger.LogInformation(
                "VerifyEmail: Email already confirmed for {UserId}",
                command.UserId);

            // نُعيد نجاحاً — لتجنب إرباك المستخدم
            return VerifyEmailResult.Ok();
        }

        // ── 3. تأكد من وجود بريد إلكتروني ─────────────────────
        if (string.IsNullOrEmpty(user.Email))
        {
            _logger.LogWarning(
                "VerifyEmail: No email set for user {UserId}",
                command.UserId);

            return VerifyEmailResult.Fail(
                "NO_EMAIL",
                "لا يوجد بريد إلكتروني مُضاف لهذا الحساب.");
        }

        // ── 4. التحقق من الرمز عبر ASP.NET Identity ──────────
        // ConfirmEmailAsync:
        //   • يتحقق من الرمز بالنسبة لـ SecurityStamp المستخدم
        //   • إذا نجح → يضبط EmailConfirmed = true في قاعدة البيانات
        //   • إذا فشل → يُعيد IdentityResult.Failed مع رسائل الخطأ
        var confirmResult = await _userManager.ConfirmEmailAsync(
            user, command.Token);

        if (!confirmResult.Succeeded)
        {
            // الأسباب الشائعة: رمز منتهي الصلاحية، رمز مستخدم مرة ثانية،
            // أو تغيير SecurityStamp بعد إنشاء الرمز
            _logger.LogWarning(
                "VerifyEmail: Confirmation failed for {UserId}. Errors: {Errors}",
                command.UserId,
                string.Join(", ", confirmResult.Errors.Select(e => e.Code)));

            return VerifyEmailResult.Fail(
                "INVALID_TOKEN",
                "رابط التحقق غير صالح أو منتهي الصلاحية. اطلب رابطاً جديداً.");
        }

        _logger.LogInformation(
            "VerifyEmail: Email {Email} confirmed for user {UserId}",
            user.Email, command.UserId);

        return VerifyEmailResult.Ok();
    }
}