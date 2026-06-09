using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PropertyApi.Domain.Auth.Entities;
using PropertyApi.Domain.Auth.Enums;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Application.Auth.Interfaces;

// ══════════════════════════════════════════════════════════════
// Interfaces — العقود التي تربط طبقة Application بالبنية التحتية
//
// لماذا Interfaces وليس Classes مباشرة؟
// ──────────────────────────────────────
// Clean Architecture: طبقة Application لا تعرف كيف يُرسَل SMS
// بالضبط. تعرف فقط أنها تستطيع طلب الإرسال. هذا يسمح لنا:
// • تبديل مزود SMS (Twilio → مزود محلي) بدون تغيير الكود
// • استخدام ConsoleSmsService في التطوير بدون إرسال فعلي
// ══════════════════════════════════════════════════════════════

/// <summary>
/// خدمة توليد والتحقق من رموز OTP
/// </summary>
public interface IOtpService
{
    /// <summary>
    /// أنشئ رمز OTP جديداً من 6 أرقام
    /// </summary>
    /// <returns>
    /// otp  → الرمز النصي "123456" للإرسال عبر SMS
    /// hash → هاش الرمز للحفظ في قاعدة البيانات
    /// </returns>
    (string otp, string hash) Generate();

    /// <summary>
    /// تحقق من صحة الرمز المُدخَل
    /// </summary>
    /// <param name="otp">الرمز المُدخَل من المستخدم</param>
    /// <param name="storedHash">الهاش المحفوظ في قاعدة البيانات</param>
    bool Verify(string otp, string storedHash);
}

/// <summary>
/// خدمة إرسال الرسائل القصيرة (SMS)
/// يمكن استبدال هذا بأي مزود: Twilio، مزود محلي سوري، إلخ
/// </summary>
public interface ISmsService
{
    /// <summary>
    /// أرسل رمز OTP للمستخدم
    /// </summary>
    /// <param name="phoneNumber">الهاتف بصيغة E.164 مثل +963911234567</param>
    /// <param name="otp">رمز الـ 6 أرقام</param>
    /// <returns>true إذا نجح الإرسال</returns>
    Task<bool> SendOtpAsync(
        string phoneNumber,
        string otp,
        CancellationToken ct = default);
}

/// <summary>
/// مستودع رموز OTP — يتعامل مع قاعدة البيانات
/// </summary>
public interface IOtpCodeRepository
{
    /// <summary>أضف رمز OTP جديداً</summary>
    Task AddAsync(OtpCode otpCode, CancellationToken ct = default);

    /// <summary>
    /// احصل على آخر رمز صالح لرقم الهاتف والغرض المحدد
    /// صالح = لم ينتهِ + لم يُستخدم + لم يُستنفَد
    /// </summary>
    Task<OtpCode?> GetLatestValidAsync(
        string phoneNumber,
        OtpPurpose purpose,
        CancellationToken ct = default);

    /// <summary>
    /// عُدّ طلبات OTP الأخيرة لرقم الهاتف خلال فترة زمنية
    /// (للتحقق من حد الطلبات)
    /// </summary>
    Task<int> CountRecentAsync(
        string phoneNumber,
        TimeSpan window,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// خدمة إرسال رسائل التحقق من البريد الإلكتروني
/// </summary>
public interface IEmailVerificationService
{
    /// <summary>أرسل رابط تحقق للبريد الإلكتروني</summary>
    Task SendVerificationLinkAsync(
        string email,
        string verificationToken,
        CancellationToken ct = default);
}