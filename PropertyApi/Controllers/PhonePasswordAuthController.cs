using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Phone;
using PropertyApi.Domain.Enums;
using PropertyApi.Security.Auth;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/auth/phone")]
// Auth responses carry tokens, challenge ids and account status: never stored by a browser or a proxy.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PhonePasswordAuthController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IJwtTokenSettings _jwtSettings;

    public PhonePasswordAuthController(ISender sender, IJwtTokenSettings jwtSettings)
    {
        _sender = sender;
        _jwtSettings = jwtSettings;
    }

    // Which delivery channels (SMS, Telegram, WhatsApp) the picker offers for a number. Anonymous by necessity: it is
    // called before any account exists. The answer depends on the public calling code and on configuration only, never on
    // whether the number has an account or a Telegram/WhatsApp profile. POST, so the number is not written into a URL.
    [HttpPost("channels"), AllowAnonymous, EnableRateLimiting("public-read")]
    public async Task<IActionResult> Channels(OtpChannelsRequest r, CancellationToken ct) => Ok(new { channels = await _sender.Send(new GetOtpChannelsQuery(r.PhoneNumber), ct) });
    [HttpPost("registration/send-otp"), AllowAnonymous, EnableRateLimiting("send-otp")]
    public Task<IActionResult> SendRegistrationOtp(PhoneNumberRequestV2 r, CancellationToken ct) => Send(r.PhoneNumber, OtpPurpose.PhoneRegistration, null, r.Channel ?? OtpChannel.Sms, ct);
    [HttpPost("registration/verify"), AllowAnonymous, EnableRateLimiting("verify-otp")]
    public async Task<IActionResult> Register(PhoneRegistrationVerifyRequest r, CancellationToken ct) => Result(await _sender.Send(new RegisterPhoneCommand(r.ChallengeId, r.Code, r.Password, r.FirstName, r.LastName, Ip(), r.Channel), ct));
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth-login")]
    public async Task<IActionResult> Login(PhoneLoginRequestV2 r, CancellationToken ct) => Result(await _sender.Send(new LoginPhoneCommand(r.PhoneNumber, r.Password, Ip()), ct));
    [HttpPost("password-reset/send-otp"), AllowAnonymous, EnableRateLimiting("auth-password-reset")]
    public Task<IActionResult> SendReset(PhoneNumberRequestV2 r, CancellationToken ct) => Send(r.PhoneNumber, OtpPurpose.PhonePasswordReset, null, r.Channel ?? OtpChannel.Sms, ct);
    [HttpPost("password-reset/verify"), AllowAnonymous, EnableRateLimiting("verify-otp")]
    public async Task<IActionResult> VerifyReset(ChallengeCodeRequest r, CancellationToken ct) => Result(await _sender.Send(new VerifyPhonePasswordResetCommand(r.ChallengeId, r.Code, r.Channel), ct));
    [HttpPost("password-reset/confirm"), AllowAnonymous, EnableRateLimiting("auth-password-reset")]
    public async Task<IActionResult> ConfirmReset(PhonePasswordResetConfirmRequest r, CancellationToken ct) => Result(await _sender.Send(new ConfirmPhonePasswordResetCommand(r.PhoneNumber, r.ConfirmationToken, r.NewPassword, Ip()), ct));
    [HttpGet("reverification/status"), Authorize]
    public async Task<IActionResult> Status(CancellationToken ct) => Id() is { } id ? Result(await _sender.Send(new GetPhoneReverificationStatusQuery(id), ct)) : Unauthorized();
    [HttpPost("reverification/send-otp"), Authorize, EnableRateLimiting("send-otp")]
    public Task<IActionResult> SendReverify(PhoneNumberRequestV2 r, CancellationToken ct) => Id() is { } id ? Send(r.PhoneNumber, OtpPurpose.PhoneReverification, id, r.Channel ?? OtpChannel.Sms, ct) : Task.FromResult<IActionResult>(Unauthorized());
    [HttpPost("reverification/verify"), Authorize, EnableRateLimiting("verify-otp")]
    public async Task<IActionResult> Reverify(ChallengeCodeRequest r, CancellationToken ct) => Id() is { } id ? Result(await _sender.Send(new VerifyPhoneReverificationCommand(id, r.ChallengeId, r.Code, r.Channel), ct)) : Unauthorized();
    [HttpPost("change/send-otp"), Authorize, EnableRateLimiting("send-otp")]
    public Task<IActionResult> SendChange(PhoneNumberRequestV2 r, CancellationToken ct) => Id() is { } id ? Send(r.PhoneNumber, OtpPurpose.PhoneNumberChange, id, r.Channel ?? OtpChannel.Sms, ct) : Task.FromResult<IActionResult>(Unauthorized());
    [HttpPost("change/verify"), Authorize, EnableRateLimiting("verify-otp")]
    public async Task<IActionResult> Change(PhoneChangeVerifyRequest r, CancellationToken ct) => Id() is { } id ? Result(await _sender.Send(new VerifyPhoneChangeCommand(id, r.ChallengeId, r.Code, r.CurrentPassword, Ip(), r.Channel), ct)) : Unauthorized();

    private async Task<IActionResult> Send(string phone, OtpPurpose purpose, Guid? id, OtpChannel channel, CancellationToken ct) => Result(await _sender.Send(new SendPhoneChallengeCommand(phone, purpose, id, Ip(), channel), ct));

    // RELEASE-BLOCKERS-AR.md B-13: this is the one place Register/Login's issued refresh
    // token would otherwise reach the JSON body. Every other PhoneWorkflowResult use
    // (SendOtp, VerifyReset, reverification, ...) carries a null RefreshToken already, so
    // checking for one here and only there needs no per-endpoint branching.
    private IActionResult Result(PhoneWorkflowResult r)
    {
        if (!r.Succeeded)
            return new BadRequestObjectResult(new { code = r.ErrorCode, message = r.Message });

        if (r.RefreshToken is { } refreshToken)
        {
            RefreshTokenCookie.Attach(Response, refreshToken, _jwtSettings.RefreshTokenDays);
            r = r with { RefreshToken = null };
        }

        return new OkObjectResult(r);
    }
    private Guid? Id() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();
}

public sealed record PhoneNumberRequestV2(string PhoneNumber, OtpChannel? Channel = null);
public sealed record OtpChannelsRequest(string? PhoneNumber);
public sealed record PhoneRegistrationVerifyRequest(Guid ChallengeId, string Code, string Password, string FirstName, string LastName, OtpChannel? Channel = null);
public sealed record PhoneLoginRequestV2(string PhoneNumber, string Password);
public sealed record ChallengeCodeRequest(Guid ChallengeId, string Code, OtpChannel? Channel = null);
public sealed record PhonePasswordResetConfirmRequest(string PhoneNumber, string ConfirmationToken, string NewPassword);
public sealed record PhoneChangeVerifyRequest(Guid ChallengeId, string Code, string CurrentPassword, OtpChannel? Channel = null);
