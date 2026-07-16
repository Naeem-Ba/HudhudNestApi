using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Auth.Phone;
using PropertyApi.Domain.Enums;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/auth/phone")]
public sealed class PhonePasswordAuthController : ControllerBase
{
    private readonly ISender _sender;
    public PhonePasswordAuthController(ISender sender) => _sender = sender;

    [HttpPost("registration/send-otp"), AllowAnonymous, EnableRateLimiting("send-otp")]
    public Task<IActionResult> SendRegistrationOtp(PhoneNumberRequestV2 r, CancellationToken ct) => Send(r.PhoneNumber, OtpPurpose.PhoneRegistration, null, ct);
    [HttpPost("registration/verify"), AllowAnonymous, EnableRateLimiting("verify-otp")]
    public async Task<IActionResult> Register(PhoneRegistrationVerifyRequest r, CancellationToken ct) => Result(await _sender.Send(new RegisterPhoneCommand(r.ChallengeId, r.Code, r.Password, r.FirstName, r.LastName, Ip()), ct));
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth-login")]
    public async Task<IActionResult> Login(PhoneLoginRequestV2 r, CancellationToken ct) => Result(await _sender.Send(new LoginPhoneCommand(r.PhoneNumber, r.Password, Ip()), ct));
    [HttpPost("password-reset/send-otp"), AllowAnonymous, EnableRateLimiting("auth-password-reset")]
    public Task<IActionResult> SendReset(PhoneNumberRequestV2 r, CancellationToken ct) => Send(r.PhoneNumber, OtpPurpose.PhonePasswordReset, null, ct);
    [HttpPost("password-reset/verify"), AllowAnonymous, EnableRateLimiting("verify-otp")]
    public async Task<IActionResult> VerifyReset(ChallengeCodeRequest r, CancellationToken ct) => Result(await _sender.Send(new VerifyPhonePasswordResetCommand(r.ChallengeId, r.Code), ct));
    [HttpPost("password-reset/confirm"), AllowAnonymous, EnableRateLimiting("auth-password-reset")]
    public async Task<IActionResult> ConfirmReset(PhonePasswordResetConfirmRequest r, CancellationToken ct) => Result(await _sender.Send(new ConfirmPhonePasswordResetCommand(r.PhoneNumber, r.ConfirmationToken, r.NewPassword, Ip()), ct));
    [HttpGet("reverification/status"), Authorize]
    public async Task<IActionResult> Status(CancellationToken ct) => Id() is { } id ? Result(await _sender.Send(new GetPhoneReverificationStatusQuery(id), ct)) : Unauthorized();
    [HttpPost("reverification/send-otp"), Authorize, EnableRateLimiting("send-otp")]
    public Task<IActionResult> SendReverify(PhoneNumberRequestV2 r, CancellationToken ct) => Id() is { } id ? Send(r.PhoneNumber, OtpPurpose.PhoneReverification, id, ct) : Task.FromResult<IActionResult>(Unauthorized());
    [HttpPost("reverification/verify"), Authorize, EnableRateLimiting("verify-otp")]
    public async Task<IActionResult> Reverify(ChallengeCodeRequest r, CancellationToken ct) => Id() is { } id ? Result(await _sender.Send(new VerifyPhoneReverificationCommand(id, r.ChallengeId, r.Code), ct)) : Unauthorized();
    [HttpPost("change/send-otp"), Authorize, EnableRateLimiting("send-otp")]
    public Task<IActionResult> SendChange(PhoneNumberRequestV2 r, CancellationToken ct) => Id() is { } id ? Send(r.PhoneNumber, OtpPurpose.PhoneNumberChange, id, ct) : Task.FromResult<IActionResult>(Unauthorized());
    [HttpPost("change/verify"), Authorize, EnableRateLimiting("verify-otp")]
    public async Task<IActionResult> Change(PhoneChangeVerifyRequest r, CancellationToken ct) => Id() is { } id ? Result(await _sender.Send(new VerifyPhoneChangeCommand(id, r.ChallengeId, r.Code, r.CurrentPassword, Ip()), ct)) : Unauthorized();

    private async Task<IActionResult> Send(string phone, OtpPurpose purpose, Guid? id, CancellationToken ct) => Result(await _sender.Send(new SendPhoneChallengeCommand(phone, purpose, id, Ip()), ct));
    private static IActionResult Result(PhoneWorkflowResult r) => r.Succeeded ? new OkObjectResult(r) : new BadRequestObjectResult(new { code = r.ErrorCode, message = r.Message });
    private Guid? Id() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();
}

public sealed record PhoneNumberRequestV2(string PhoneNumber);
public sealed record PhoneRegistrationVerifyRequest(Guid ChallengeId, string Code, string Password, string FirstName, string LastName);
public sealed record PhoneLoginRequestV2(string PhoneNumber, string Password);
public sealed record ChallengeCodeRequest(Guid ChallengeId, string Code);
public sealed record PhonePasswordResetConfirmRequest(string PhoneNumber, string ConfirmationToken, string NewPassword);
public sealed record PhoneChangeVerifyRequest(Guid ChallengeId, string Code, string CurrentPassword);
