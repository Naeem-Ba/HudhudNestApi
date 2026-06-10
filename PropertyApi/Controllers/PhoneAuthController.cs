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
using PropertyApi.Domain.Enums;

namespace PropertyApi.Controllers;

/// <summary>
/// ???????? ??? ??? ?????? ?????? ?????? ??????????
///
/// ???? ??????? (Endpoints):
/// -------------------------
/// POST /api/auth/phone/send-otp     ? ????? ??? ?????? ??????
/// POST /api/auth/phone/verify       ? ?????? ?? ????? (????? ?? ????)
/// POST /api/auth/email/add          ? ????? ???? ???????? ??????
/// POST /api/auth/email/verify       ? ????? ?????? ??????????
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
    // POST /api/auth/phone/send-otp
    // ----------------------------------------------------------
    /// <summary>????? ??? OTP ???? ??????</summary>
    /// <remarks>
    /// ?????? ?????? ?? ????? ??????? ?? ??????.
    /// ?????? ???????? ????? SMS ????? ??? ??? ????? ?? 6 ?????.
    ///
    /// **??????:**
    /// - 3 ????? ??? ???? ?? ???? ???? ?????
    /// - 10 ????? ??? ???? ?? ????? ?? ??? ??? IP
    ///
    /// **????:**
    /// ```json
    /// POST /api/auth/phone/send-otp
    /// { "phoneNumber": "+963911234567" }
    /// ```
    /// </remarks>
    [HttpPost("phone/send-otp")]
    [AllowAnonymous]
    [EnableRateLimiting("send-otp")]  // ????? ??? ?? Program.cs
    [ProducesResponseType(typeof(SendOtpResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendOtp(
        [FromBody] SendOtpRequest request,
        CancellationToken ct)
    {
        var command = new SendPhoneOtpCommand(
            PhoneNumber: request.PhoneNumber,
            Purpose: request.Purpose,
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString());

        var result = await _mediator.Send(command, ct);

        if (!result.Success)
        {
            // 429 = Too Many Requests (?? ???????)
            var statusCode = result.ErrorCode == "RATE_LIMITED"
                ? StatusCodes.Status429TooManyRequests
                : StatusCodes.Status400BadRequest;

            return StatusCode(statusCode, new ErrorResponse(
                result.ErrorCode ?? "ERROR",
                result.ErrorMessage ?? "??? ???"));
        }

        return Ok(new SendOtpResponse(
            Message: "?? ????? ??? ?????? ?????.",
            ExpiresInSeconds: 300 // 5 ?????
        ));
    }

    // ----------------------------------------------------------
    // POST /api/auth/phone/verify
    // ----------------------------------------------------------
    /// <summary>?????? ?? ??? OTP ??????? ?? ???????</summary>
    /// <remarks>
    /// ?????? ??????? ????????.
    ///
    /// **????????? 1 — ?????? ????:**
    /// - ????? ???? ???? ????????
    /// - IsNewUser = true ?? ?????????
    ///
    /// **????????? 2 — ?????? ?????:**
    /// - ????? ???? ?????
    /// - IsNewUser = false ?? ?????????
    ///
    /// **????:**
    /// ```json
    /// POST /api/auth/phone/verify
    /// {
    ///   "phoneNumber": "+963911234567",
    ///   "code": "123456",
    ///   "firstName": "????",  // ??????? ?????????? ?????
    ///   "lastName":  "??????"
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
    Purpose: request.Purpose,
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
                result.ErrorMessage ?? "??? ??????"));
        }

        return Ok(new AuthResponse(
            IsNewUser: result.IsNewUser,
            AccessToken: result.AccessToken!,
            RefreshToken: result.RefreshToken!,
            ExpiresAt: result.AccessTokenExpiresAt!.Value,
            User: MapToUserDto(result.User!)));
    }

    // ----------------------------------------------------------
    // POST /api/auth/email/add
    // ----------------------------------------------------------
    /// <summary>????? ???? ???????? ?????? (?????????? ??????????? ???? ??????)</summary>
    /// <remarks>
    /// ????? ????? ?????? ????? (JWT Token ?? ??? Header).
    ///
    /// **????:**
    /// ```json
    /// POST /api/auth/email/add
    /// Authorization: Bearer eyJhbGci...
    /// { "email": "ahmed@example.com" }
    /// ```
    /// </remarks>
    [HttpPost("email/add")]
    [Authorize] // ????? ????? ??????
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AddEmail(
        [FromBody] AddEmailRequest request,
        CancellationToken ct)
    {
        // ?????? ????? ???????? ?? ??? JWT Token
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(new ErrorResponse("UNAUTHORIZED", "??? ????."));

        var command = new AddEmailCommand(userId.Value, request.Email);
        var result = await _mediator.Send(command, ct);

        if (!result.Success)
            return BadRequest(new ErrorResponse(
                result.ErrorCode ?? "ERROR",
                result.ErrorMessage ?? "??? ????? ??????"));

        return Ok(new MessageResponse(
            "?? ????? ????? ???? ?????? ??????????. ???? ?? ????? ??????."));
    }

    // ----------------------------------------------------------
    // POST /api/auth/email/verify
    // ----------------------------------------------------------
    /// <summary>????? ?????? ?????????? ??? ??? ??????</summary>
    /// <remarks>
    /// ??????? ??? ??? ???????? ??? ???? ?????? ?? ?????.
    ///
    /// **????:**
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

        return Ok(new MessageResponse("?? ????? ????? ?????????? ?????!"));
    }

    // -- ???? ??????: ??????? ????? ???????? ?? ??? JWT --------
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
// Request / Response Records (DTOs ??? HTTP layer)
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