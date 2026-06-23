using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PropertyApi.Application.Auth.Commands.ForgotPassword;
using PropertyApi.Application.Auth.Commands.Login;
using PropertyApi.Application.Auth.Commands.Logout;
using PropertyApi.Application.Auth.Commands.RefreshToken;
using PropertyApi.Application.Auth.Commands.Register;
using PropertyApi.Application.Auth.Commands.ResetPassword;
using PropertyApi.Application.Auth.Commands.SocialLogin;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly ISender _sender;

    public AuthController(ISender sender)
        => _sender = sender;

    // POST /api/auth/register
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest dto,
        CancellationToken ct)
    {
        var result = await _sender.Send(new RegisterCommand(
            FirstName: dto.FirstName,
            LastName: dto.LastName,
            Email: dto.Email,
            Password: dto.Password), ct);

        if (result.Conflict)
            return Conflict(new { message = result.Message });

        if (!result.Success)
            return BadRequest(new { errors = result.Errors });

        return StatusCode(StatusCodes.Status201Created, new
        {
            message = result.Message,
            userId = result.UserId
        });
    }

    // POST /api/auth/login
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest dto,
        CancellationToken ct)
    {
        var result = await _sender.Send(new LoginCommand(
            Email: dto.Email,
            Password: dto.Password,
            IpAddress: GetClientIp()), ct);

        if (!result.Success)
            return Unauthorized(new { message = result.Message });

        return Ok(new
        {
            accessToken = result.AccessToken,
            refreshToken = result.RefreshToken,
            expiresIn = result.ExpiresIn
        });
    }

    // POST /api/auth/forgot-password
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-password-reset")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest dto,
        CancellationToken ct)
    {
        var result = await _sender.Send(new ForgotPasswordCommand(
            Email: dto.Email,
            RequestScheme: Request.Scheme,
            RequestHost: Request.Host.Value), ct);

        if (!result.Success)
            return BadRequest(new { message = result.Message });

        return Ok(new { message = result.Message });
    }

    // POST /api/auth/reset-password
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-password-reset")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest dto,
        CancellationToken ct)
    {
        var result = await _sender.Send(new ResetPasswordCommand(
            Email: dto.Email,
            Token: dto.Token,
            NewPassword: dto.NewPassword,
            ConfirmPassword: dto.ConfirmPassword,
            IpAddress: GetClientIp()), ct);

        if (result.Conflict)
            return Conflict(new { message = result.Message });

        if (!result.Success)
        {
            if (result.Errors.Count > 0)
                return BadRequest(new { errors = result.Errors });

            return BadRequest(new { message = result.Message });
        }

        return Ok(new { message = result.Message });
    }

    // POST /api/auth/refresh
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest dto,
        CancellationToken ct)
    {
        var result = await _sender.Send(new RefreshTokenCommand(
            RefreshToken: dto.RefreshToken,
            IpAddress: GetClientIp()), ct);

        if (!result.Success)
            return Unauthorized(new { message = result.Message });

        return Ok(new
        {
            accessToken = result.AccessToken,
            refreshToken = result.RefreshToken,
            expiresIn = result.ExpiresIn
        });
    }

    // POST /api/auth/logout
    [Authorize]
    [HttpPost("logout")]
    [EnableRateLimiting("auth-logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(
        [FromBody] RefreshRequest dto,
        CancellationToken ct)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdText, out var userId))
            return Unauthorized();

        await _sender.Send(new LogoutCommand(
            UserId: userId,
            RefreshToken: dto.RefreshToken,
            IpAddress: GetClientIp()), ct);

        return NoContent();
    }

    // POST /api/auth/social/google
    [HttpPost("social/google")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SocialLoginGoogle(
        [FromBody] GoogleLoginRequest dto,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.IdToken))
            return BadRequest(new { message = "idToken is required." });

        var result = await _sender.Send(new SocialLoginCommand(
            GoogleIdToken: dto.IdToken,
            AppleIdentityToken: null,
            AppleAuthorizationCode: null,
            AppleFirstName: null,
            AppleLastName: null,
            IpAddress: GetClientIp()), ct);

        if (!result.Success)
            return BadRequest(new { message = result.Message });

        return Ok(new
        {
            accessToken = result.AccessToken,
            refreshToken = result.RefreshToken,
            expiresIn = result.ExpiresIn
        });
    }

    // POST /api/auth/social/apple
    [HttpPost("social/apple")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SocialLoginApple(
        [FromBody] AppleLoginRequest dto,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.IdentityToken))
            return BadRequest(new { message = "identityToken is required." });

        var result = await _sender.Send(new SocialLoginCommand(
            GoogleIdToken: null,
            AppleIdentityToken: dto.IdentityToken,
            AppleAuthorizationCode: dto.AuthorizationCode,
            AppleFirstName: dto.FirstName,
            AppleLastName: dto.LastName,
            IpAddress: GetClientIp()), ct);

        if (!result.Success)
            return BadRequest(new { message = result.Message });

        return Ok(new
        {
            accessToken = result.AccessToken,
            refreshToken = result.RefreshToken,
            expiresIn = result.ExpiresIn
        });
    }
    private string? GetClientIp()
        => HttpContext.Connection.RemoteIpAddress?.ToString();
}

// HTTP request DTOs kept in API layer to preserve the public API shape.
public sealed record RegisterRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password);

public sealed record LoginRequest(
    string Email,
    string Password);

public sealed record GoogleLoginRequest(
    string IdToken);

public sealed record AppleLoginRequest(
    string IdentityToken,
    string? AuthorizationCode,
    string? FirstName,
    string? LastName);
public sealed record RefreshRequest(
    string RefreshToken);

public sealed record ForgotPasswordRequest(
    string Email);

public sealed record ResetPasswordRequest(
    string Email,
    string Token,
    string NewPassword,
    string ConfirmPassword);




