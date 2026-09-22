using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using HudhudNestApi.Application.Auth.Commands.AddEmail;
using HudhudNestApi.Application.Auth.Commands.ResendConfirmationEmail;
using HudhudNestApi.Application.Auth.Commands.VerifyEmail;
using HudhudNestApi.Application.Auth.DTOs;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Controllers;

/// <summary>
/// المصادقة عبر رقم الهاتف وإدارة البريد الإلكتروني
///
/// نقاط النهاية الحالية (Endpoints):
/// ──────────────────────────────────
/// POST /api/auth/email/add                 → إضافة بريد إلكتروني للحساب
/// POST /api/auth/email/verify              → تفعيل البريد الإلكتروني
/// POST /api/auth/email/resend-confirmation → إعادة إرسال رابط التأكيد
///
/// POST /api/auth/phone/send-otp و POST /api/auth/phone/verify أصبحتا 410 Gone؛
/// التسجيل/الدخول عبر الهاتف الآن من PhonePasswordAuthController.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class PhoneAuthController : ControllerBase
{
    private readonly ISender _mediator;

    public PhoneAuthController(ISender mediator)
        => _mediator = mediator;


    // ----------------------------------------------------------
    // Removed: POST /api/auth/phone/send-otp
    // Removed: POST /api/auth/phone/verify
    // ----------------------------------------------------------
    // The legacy "OTP is the credential" flow was replaced by
    // PhonePasswordAuthController (registration/send-otp, registration/verify,
    // login, password-reset/*). Its two actions are gone.
    //
    // 410 is kept rather than letting the routes 404, because an integration test
    // and any still-deployed client both rely on "permanently gone" being told
    // apart from "wrong URL".
    //
    // SECURITY FIX: that 410 used to come from LegacyPhoneOtpDeprecationMiddleware,
    // which compared Request.Path to two literal strings *outside* routing. Routing
    // matches a trailing slash, a literal comparison does not, so a request to
    // /api/auth/phone/send-otp/ walked straight past the guard and reached the live
    // legacy action. Routing now owns the decision, so there is no string to slip past.
    [HttpPost("phone/send-otp")]
    [HttpPost("phone/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("public-read")]
    [ApiExplorerSettings(IgnoreApi = true)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public IActionResult LegacyPhoneOtpFlowRemoved()
        => StatusCode(StatusCodes.Status410Gone, new
        {
            code = "PHONE_OTP_FLOW_DEPRECATED",
            message = "Use the phone registration or password login endpoints."
        });

    // ----------------------------------------------------------
    // POST /api/auth/email/add
    // ----------------------------------------------------------
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
            return Unauthorized(new ErrorResponse("UNAUTHORIZED", "غير مصرّح."));

        var command = new AddEmailCommand(userId.Value, request.Email);
        var result = await _mediator.Send(command, ct);

        if (!result.Success)
            return BadRequest(new ErrorResponse(
                result.ErrorCode ?? "ERROR",
                result.ErrorMessage ?? "تعذّر إضافة البريد."));

        return Ok(new MessageResponse(
            "تم إرسال رابط تأكيد البريد الإلكتروني. تفقّد صندوق الوارد."));
    }

    // ----------------------------------------------------------
    // POST /api/auth/email/verify
    // ----------------------------------------------------------
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
    [EnableRateLimiting("auth-password-reset")]
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

        return Ok(new MessageResponse("تم تأكيد بريدك الإلكتروني بنجاح."));
    }

    // ----------------------------------------------------------
    // POST /api/auth/email/resend-confirmation
    // ----------------------------------------------------------
    /// <summary>Sends the email-confirmation link again.</summary>
    /// <remarks>
    /// Anonymous by necessity: the caller is someone who cannot finish signing up because
    /// the first link never arrived, and gating this on a token would exclude exactly the
    /// accounts that need it.
    ///
    /// The response is identical whether the address is unknown, already confirmed, or
    /// was just sent to, so it cannot be used to discover which addresses hold accounts.
    ///
    /// ```json
    /// POST /api/auth/email/resend-confirmation
    /// { "email": "ahmed@example.com" }
    /// ```
    /// </remarks>
    [HttpPost("email/resend-confirmation")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-password-reset")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendConfirmation(
        [FromBody] ResendConfirmationRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(
            new ResendConfirmationEmailCommand(request.Email),
            ct);

        return Ok(new MessageResponse(result.Message));
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

// --------------------------------------------------------------
// Request / Response Records (DTOs للـ HTTP layer)
// --------------------------------------------------------------

// -- Requests --------------------------------------------------
public sealed record SendOtpRequest(
    string PhoneNumber,
    OtpPurpose Purpose = OtpPurpose.PhoneRegistration);
public sealed record VerifyOtpRequest(
    string PhoneNumber,
    string Code,
    OtpPurpose Purpose = OtpPurpose.PhoneRegistration,
    string? FirstName = null,
    string? LastName = null);

public sealed record AddEmailRequest(string Email);

public sealed record VerifyEmailRequest(Guid UserId, string Token);

public sealed record ResendConfirmationRequest(string Email);

// -- Responses -------------------------------------------------
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
