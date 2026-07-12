using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PropertyApi.Application.Auth.DTOs;

// ══════════════════════════════════════════════════════════════
// DTOs (Data Transfer Objects) لنظام المصادقة بالهاتف
//
// ما هو الـ DTO؟
// ─────────────
// كبسولة بيانات تنتقل من الـ Application layer للـ API layer.
// لا يحتوي على منطق — فقط بيانات منظمة.
// ══════════════════════════════════════════════════════════════

/// <summary>
/// نتيجة إرسال رمز OTP
/// </summary>
public sealed record SendOtpResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>الوقت بالثواني قبل السماح بإرسال رمز جديد</summary>
    public int RetryAfterSeconds { get; init; }

    public static SendOtpResult Ok() => new() { Success = true };

    public static SendOtpResult Fail(string code, string message, int retryAfter = 0) =>
        new() { Success = false, ErrorCode = code, ErrorMessage = message, RetryAfterSeconds = retryAfter };
}

/// <summary>
/// نتيجة التحقق من رمز OTP
/// تُعاد لكل من: التسجيل الجديد وتسجيل الدخول
/// </summary>
public sealed record VerifyOtpResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>هل هذا مستخدم جديد (تسجيل) أم موجود (دخول)؟</summary>
    public bool IsNewUser { get; init; }

    /// <summary>رمز الوصول JWT — صالح 60 دقيقة</summary>
    public string? AccessToken { get; init; }

    /// <summary>رمز التجديد — صالح 30 يوماً</summary>
    public string? RefreshToken { get; init; }

    public DateTime? AccessTokenExpiresAt { get; init; }

    /// <summary>ملف المستخدم المختصر</summary>
    public UserProfileDto? User { get; init; }

    public static VerifyOtpResult Ok(
        bool isNewUser,
        string accessToken,
        string refreshToken,
        DateTime expiresAt,
        UserProfileDto user) =>
        new()
        {
            Success = true,
            IsNewUser = isNewUser,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAt = expiresAt,
            User = user,
        };

    public static VerifyOtpResult Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, ErrorMessage = message };
}

/// <summary>
/// ملف المستخدم المُعاد في ردود المصادقة
/// </summary>
public sealed record UserProfileDto
{
    public Guid Id { get; init; }
    public string PhoneNumber { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }

    /// <summary>هل ربط المستخدم بريده الإلكتروني؟</summary>
    public bool HasEmail { get; init; }

    /// <summary>هل كلمة مرور مُعيَّنة؟ (يمكن للمستخدم تعيينها لاحقاً)</summary>
    public bool HasPassword { get; init; }

    public bool EmailVerified { get; init; }
}

/// <summary>
/// نتيجة إضافة البريد الإلكتروني
/// </summary>
public sealed record AddEmailResult
{
    public bool Success { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>هل تم إرسال رسالة تحقق؟</summary>
    public bool VerificationSent { get; init; }

    public static AddEmailResult Ok() =>
        new() { Success = true, VerificationSent = true };

    public static AddEmailResult Fail(string code, string message) =>
        new() { Success = false, ErrorCode = code, ErrorMessage = message };
}
