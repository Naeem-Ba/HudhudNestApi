using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Auth.Commands.AddEmail;
using PropertyApi.Application.Auth.Commands.SendPhoneOtp;
using PropertyApi.Application.Auth.Commands.VerifyEmail;
using PropertyApi.Application.Auth.Commands.VerifyPhoneOtp;
using PropertyApi.Application.Auth.DTOs;
using PropertyApi.Domain.Auth.Enums;

namespace PropertyApi.Controllers;

/// <summary>
/// المصادقة عبر رقم الهاتف وإدارة البريد الإلكتروني
///
/// نقاط النهاية (Endpoints):
/// ─────────────────────────
/// POST /api/auth/phone/send-otp     → إرسال رمز التحقق للهاتف
/// POST /api/auth/phone/verify       → التحقق من الرمز (تسجيل أو دخول)
/// POST /api/auth/email/add          → إضافة بريد إلكتروني للحساب
/// POST /api/auth/email/verify       → تفعيل البريد الإلكتروني
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class PhoneAuthController : ControllerBase
{
    private readonly ISender _mediator;

    public PhoneAuthController(ISender mediator)
        => _mediator = mediator;

    // ══════════════════════════════════════════════════════════
    // POST /api/auth/phone/send-otp
    // ══════════════════════════════════════════════════════════
    /// <summary>إرسال رمز OTP لرقم الهاتف</summary>
    /// <remarks>
    /// الخطوة الأولى من عملية التسجيل أو الدخول.
    /// سيتلقى المستخدم رسالة SMS تحتوي على رمز مكوّن من 6 أرقام.
    ///
    /// **الحدود:**
    /// - 3 طلبات كحد أقصى كل ساعة لنفس الرقم
    /// - 10 طلبات كحد أقصى كل دقيقة من نفس الـ IP
    ///
    /// **مثال:**
    /// ```json
    /// POST /api/auth/phone/send-otp
    /// { "phoneNumber": "+963911234567" }
    /// ```
    /// </remarks>
    [HttpPost("phone/send-otp")]
    [AllowAnonymous]
    [EnableRateLimiting("send-otp")]  // يحتاج ضبط في Program.cs
    [ProducesResponseType(typeof(SendOtpResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendOtp(
        [FromBody] SendOtpRequest request,
        CancellationToken ct)
    {
        var command = new SendPhoneOtpCommand(
            PhoneNumber: request.PhoneNumber,
            Purpose: OtpPurpose.PhoneRegistration,
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        var result = await _mediator.Send(command, ct);

        if (!result.Success)
        {
            // 429 = Too Many Requests (حد الطلبات)
            var statusCode = result.ErrorCode == "RATE_LIMITED"
                ? StatusCodes.Status429TooManyRequests
                : StatusCodes.Status400BadRequest;

            return StatusCode(statusCode, new ErrorResponse(
                result.ErrorCode ?? "ERROR",
                result.ErrorMessage ?? "حدث خطأ"));
        }

        return Ok(new SendOtpResponse(
            Message: "تم إرسال رمز التحقق بنجاح.",
            ExpiresInSeconds: 300 // 5 دقائق
        ));
    }

    // ══════════════════════════════════════════════════════════
    // POST /api/auth/phone/verify
    // ══════════════════════════════════════════════════════════
    /// <summary>التحقق من رمز OTP والدخول أو التسجيل</summary>
    /// <remarks>
    /// الخطوة الثانية والأخيرة.
    ///
    /// **السيناريو 1 — مستخدم جديد:**
    /// - يُنشأ حساب جديد تلقائياً
    /// - IsNewUser = true في الاستجابة
    ///
    /// **السيناريو 2 — مستخدم موجود:**
    /// - تسجيل دخول مباشر
    /// - IsNewUser = false في الاستجابة
    ///
    /// **مثال:**
    /// ```json
    /// POST /api/auth/phone/verify
    /// {
    ///   "phoneNumber": "+963911234567",
    ///   "code": "123456",
    ///   "firstName": "أحمد",  // اختياري للمستخدمين الجدد
    ///   "lastName":  "الشمري"
    /// }
    /// ```
    /// </remarks>
    [HttpPost("phone/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("verify-otp")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> VerifyOtp(
        [FromBody] VerifyOtpRequest request,
        CancellationToken ct)
    {
        var command = new VerifyPhoneOtpCommand(
            PhoneNumber: request.PhoneNumber,
            Code: request.Code,
            FirstName: request.FirstName,
            LastName: request.LastName,
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        var result = await _mediator.Send(command, ct);

        if (!result.Success)
        {
            var statusCode = result.ErrorCode switch
            {
                "OTP_WRONG" => StatusCodes.Status401Unauthorized,
                "OTP_INVALID" => StatusCodes.Status401Unauthorized,
                _ => StatusCodes.Status400BadRequest,
            };

            return StatusCode(statusCode, new ErrorResponse(
                result.ErrorCode ?? "ERROR",
                result.ErrorMessage ?? "فشل التحقق"));
        }

        return Ok(new AuthResponse(
            IsNewUser: result.IsNewUser,
            AccessToken: result.AccessToken!,
            RefreshToken: result.RefreshToken!,
            ExpiresAt: result.AccessTokenExpiresAt!.Value,
            User: MapToUserDto(result.User!)));
    }

    // ══════════════════════════════════════════════════════════
    // POST /api/auth/email/add
    // ══════════════════════════════════════════════════════════
    /// <summary>إضافة بريد إلكتروني للحساب (للمستخدمين المُسجَّلين برقم الهاتف)</summary>
    /// <remarks>
    /// يتطلب تسجيل الدخول أولاً (JWT Token في الـ Header).
    ///
    /// **مثال:**
    /// ```json
    /// POST /api/auth/email/add
    /// Authorization: Bearer eyJhbGci...
    /// { "email": "ahmed@example.com" }
    /// ```
    /// </remarks>
    [HttpPost("email/add")]
    [Authorize] // يتطلب تسجيل الدخول
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AddEmail(
        [FromBody] AddEmailRequest request,
        CancellationToken ct)
    {
        // نستخرج معرّف المستخدم من الـ JWT Token
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(new ErrorResponse("UNAUTHORIZED", "غير مصرح."));

        var command = new AddEmailCommand(userId.Value, request.Email);
        var result = await _mediator.Send(command, ct);

        if (!result.Success)
            return BadRequest(new ErrorResponse(
                result.ErrorCode ?? "ERROR",
                result.ErrorMessage ?? "فشل إضافة البريد"));

        return Ok(new MessageResponse(
            "تم إرسال رسالة تحقق لبريدك الإلكتروني. تحقق من صندوق الوارد."));
    }

    // ══════════════════════════════════════════════════════════
    // POST /api/auth/email/verify
    // ══════════════════════════════════════════════════════════
    /// <summary>تفعيل البريد الإلكتروني عبر رمز التحقق</summary>
    /// <remarks>
    /// يُستدعى عند نقر المستخدم على رابط التحقق في بريده.
    ///
    /// **مثال:**
    /// ```json
    /// POST /api/auth/email/verify
    /// { "userId": "uuid...", "token": "CfDJ8..." }
    /// ```
    /// </remarks>
    [HttpPost("email/verify")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> VerifyEmail(
        [FromBody] VerifyEmailRequest request,
        CancellationToken ct)
    {
        var command = new VerifyEmailCommand(request.UserId, request.Token);
        var result = await _mediator.Send(command, ct);

        if (!result.Success)
            return BadRequest(new ErrorResponse("VERIFY_FAILED", result.ErrorMessage!));

        return Ok(new MessageResponse("تم تفعيل بريدك الإلكتروني بنجاح!"));
    }

    // ── دالة مساعدة: استخراج معرّف المستخدم من الـ JWT ────────
    private Guid? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idClaim, out var id) ? id : null;
    }

    private static PhoneAuthUserDto MapToUserDto(UserProfileDto profile) =>
        new(
            profile.Id,
            profile.PhoneNumber,
            profile.Email,
            profile.DisplayName,
            profile.HasEmail,
            profile.HasPassword,
            profile.EmailVerified);
}

// ══════════════════════════════════════════════════════════════
// Request / Response Records (DTOs للـ HTTP layer)
// ══════════════════════════════════════════════════════════════

// ── Requests ──────────────────────────────────────────────────
public sealed record SendOtpRequest(string PhoneNumber);

public sealed record VerifyOtpRequest(
    string PhoneNumber,
    string Code,
    string? FirstName = null,
    string? LastName = null);

public sealed record AddEmailRequest(string Email);

public sealed record VerifyEmailRequest(Guid UserId, string Token);

// ── Responses ─────────────────────────────────────────────────
public sealed record SendOtpResponse(string Message, int ExpiresInSeconds);

public sealed record AuthResponse(
    bool IsNewUser,
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    PhoneAuthUserDto User);

public sealed record PhoneAuthUserDto(
    Guid Id,
    string PhoneNumber,
    string? Email,
    string? DisplayName,
    bool HasEmail,
    bool HasPassword,
    bool EmailVerified);
public sealed record MessageResponse(string Message);

public sealed record ErrorResponse(string Code, string Message);