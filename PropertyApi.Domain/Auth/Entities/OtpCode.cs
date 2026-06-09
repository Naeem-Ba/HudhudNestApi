using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PropertyApi.Domain.Auth.Enums;

namespace PropertyApi.Domain.Auth.Entities;

/// <summary>
/// كيان رمز OTP في طبقة Domain
///
/// ما هو هذا الكيان؟
/// ─────────────────
/// يُمثّل رمز التحقق المرسل عبر SMS.
/// كل رمز يُحفظ مرةً واحدة في قاعدة البيانات،
/// ويصبح غير صالح بعد:
///   • انتهاء صلاحيته (5 دقائق)
///   • استخدامه مرةً واحدة
///   • تجاوز 3 محاولات خاطئة
///
/// لماذا نحفظ هاش الرمز وليس الرمز نفسه؟
/// ──────────────────────────────────────
/// إذا اخترق أحد قاعدة البيانات لن يجد الأرقام الحقيقية.
/// يشبه تماماً طريقة حفظ كلمات المرور المشفرة.
/// </summary>
public sealed class OtpCode
{
    // ── المُنشئ خاص — لا أحد يصنع الكيان مباشرة ──────────────────
    // القاعدة: المسار الوحيد لإنشاء OtpCode هو الدالة Create أدناه
    private OtpCode() { }

    // ── الخصائص ──────────────────────────────────────────────────
    public Guid Id { get; private set; }
    public string PhoneNumber { get; private set; } = string.Empty;

    /// <summary>
    /// هاش الرمز — لا نحفظ "123456" بل نحفظ هاشه
    /// </summary>
    public string CodeHash { get; private set; } = string.Empty;
    public OtpPurpose Purpose { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    /// <summary>عدد المحاولات الخاطئة — يُمنع بعد 3</summary>
    public int AttemptCount { get; private set; }
    public bool IsUsed { get; private set; }

    /// <summary>عنوان IP لأغراض كشف الاحتيال</summary>
    public string? IpAddress { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // ── منطق العمل (Domain Logic) ─────────────────────────────────

    /// <summary>هل انتهت صلاحية الرمز؟</summary>
    public bool IsExpired() => DateTime.UtcNow > ExpiresAt;

    /// <summary>هل تجاوز عدد المحاولات المسموح (3 محاولات)؟</summary>
    public bool IsExhausted() => AttemptCount >= 3;

    /// <summary>هل الرمز صالح للاستخدام؟ (لم ينتهِ + لم يُستخدم + لم يُستنفَد)</summary>
    public bool IsValid() => !IsExpired() && !IsUsed && !IsExhausted();

    /// <summary>الوقت المتبقي بالثواني قبل انتهاء الصلاحية</summary>
    public int SecondsRemaining() =>
        Math.Max(0, (int)(ExpiresAt - DateTime.UtcNow).TotalSeconds);

    /// <summary>سجّل محاولة خاطئة</summary>
    public void IncrementAttempts() => AttemptCount++;

    /// <summary>ضع علامة "تم الاستخدام" — يمنع إعادة الاستخدام</summary>
    public void MarkAsUsed() => IsUsed = true;

    // ── Factory Method — الطريقة الوحيدة لإنشاء كيان OtpCode ───────
    /// <summary>
    /// أنشئ رمز OTP جديداً
    /// </summary>
    /// <param name="phoneNumber">رقم الهاتف بصيغة E.164 مثل +963911234567</param>
    /// <param name="codeHash">هاش الرمز (وليس الرمز نفسه)</param>
    /// <param name="purpose">الغرض: تسجيل / دخول / إضافة هاتف</param>
    /// <param name="ipAddress">عنوان IP للحماية (اختياري)</param>
    /// <param name="expiryMinutes">مدة الصلاحية بالدقائق (الافتراضي: 5)</param>
    public static OtpCode Create(
        string phoneNumber,
        string codeHash,
        OtpPurpose purpose,
        string? ipAddress = null,
        int expiryMinutes = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeHash);

        return new OtpCode
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phoneNumber.Trim(),
            CodeHash = codeHash,
            Purpose = purpose,
            ExpiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes),
            AttemptCount = 0,
            IsUsed = false,
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow,
        };
    }
}
