using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Infrastructure.Auth.Services;

/// <summary>
/// خدمة توليد والتحقق من رموز OTP
///
/// كيف يعمل الأمان؟
/// ─────────────────
/// • نُولِّد رقماً عشوائياً آمناً بـ RandomNumberGenerator (أقوى من Random)
/// • نُحوِّله لهاش HMAC-SHA256 مع مفتاح سري من الإعدادات
/// • نحفظ الهاش فقط — الرمز الحقيقي لا يُحفَظ أبداً
///
/// لماذا HMAC-SHA256 وليس bcrypt؟
/// ─────────────────────────────
/// bcrypt بطيء جداً (تصميم متعمد لكلمات المرور).
/// لكن OTP محمية بـ:
///   • مدة صلاحية قصيرة (5 دقائق)
///   • حد المحاولات (3 فقط)
///   • استخدام مرة واحدة
/// لذا HMAC-SHA256 كافٍ وأسرع.
/// </summary>
public sealed class OtpService : IOtpService
{
    private readonly byte[] _secretKey;

    public OtpService(IConfiguration config)
    {
        // المفتاح السري من appsettings.json أو User Secrets
        // مثال: "OtpSettings:SecretKey": "your-secret-key-min-32-chars"
        var key = config["OtpSettings:SecretKey"]
            ?? throw new InvalidOperationException(
                "OtpSettings:SecretKey is required in configuration.");

        if (key.Length < 32)
            throw new InvalidOperationException(
                "OtpSettings:SecretKey must be at least 32 characters.");

        _secretKey = Encoding.UTF8.GetBytes(key);
    }

    /// <summary>
    /// أنشئ رمز OTP جديداً
    /// </summary>
    public (string otp, string hash) Generate()
    {
        // RandomNumberGenerator → آمن تشفيرياً (أقوى من Random)
        var bytes = new byte[4];
        RandomNumberGenerator.Fill(bytes);

        // نحوّله لرقم من 0-999999 ثم نملأه بالأصفار لنضمن 6 أرقام
        var number = Math.Abs(BitConverter.ToInt32(bytes, 0)) % 1_000_000;
        var otp = number.ToString("D6"); // "D6" = 6 أرقام مع أصفار إذا لزم

        var hash = ComputeHash(otp);

        return (otp, hash);
    }

    /// <summary>
    /// تحقق من تطابق رمز OTP مع الهاش
    /// </summary>
    public bool Verify(string otp, string storedHash)
    {
        if (string.IsNullOrWhiteSpace(otp) || string.IsNullOrWhiteSpace(storedHash))
            return false;

        var computedHash = ComputeHash(otp);

        // CryptographicOperations.FixedTimeEquals → مقارنة في وقت ثابت
        // تمنع هجوم Timing Attack
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHash),
            Encoding.UTF8.GetBytes(storedHash));
    }

    // ── دالة حساب الهاش الداخلية ──────────────────────────────
    private string ComputeHash(string otp)
    {
        using var hmac = new HMACSHA256(_secretKey);
        var data = Encoding.UTF8.GetBytes(otp);
        var hash = hmac.ComputeHash(data);
        return Convert.ToBase64String(hash);
    }
}