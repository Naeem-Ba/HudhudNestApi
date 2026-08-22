using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Commands.ForgotPassword;
using PropertyApi.Application.Auth.Commands.Login;
using PropertyApi.Application.Auth.Commands.Logout;
using PropertyApi.Application.Auth.Commands.RefreshToken;
using PropertyApi.Application.Auth.Commands.Register;
using PropertyApi.Application.Auth.Commands.ResetPassword;
using PropertyApi.Application.Auth.Commands.SocialLogin;
using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly ISender _sender;
    private readonly ILogger<AuthController> _logger;

    // ILoginIdentityService was injected only so the login failure path could look the
    // account up and report its attempt count and lockout state. That lookup is gone
    // (see the Login method), and with it the controller's reason to reach past MediatR
    // into the identity store at all.
    public AuthController(
        ISender sender,
        ILogger<AuthController> logger)
    {
        _sender = sender;
        _logger = logger;
    }

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

    /// <summary>
    /// Authenticates a user with email and password.
    ///
    /// Every failure -- unknown address, wrong password, locked account -- answers with
    /// the same 401 and the same body, so the response cannot be used to discover which
    /// addresses are registered. Lockout state reaches the account owner by email.
    ///
    /// Status codes: 200 Success, 401 Authentication failed.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponseDto>> Login(
        [FromBody] LoginRequest dto,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(new LoginResponseDto
            {
                Success = false,
                StatusCode = 400,
                Message = "Invalid request parameters",
                ErrorCode = LoginErrorCodes.InvalidRequest
            });
        }

        try
        {
            var ipAddress = GetClientIp();
            var result = await _sender.Send(new LoginCommand(
                Email: dto.Email,
                Password: dto.Password,
                IpAddress: ipAddress), ct);

            if (result.Success)
            {
                _logger.LogInformation(
                    "Login succeeded from IP {IpAddress}.",
                    ipAddress);

                var response = LoginResponseDto.CreateSuccess(
                    result.AccessToken,
                    result.RefreshToken,
                    result.ExpiresIn);

                return Ok(response);
            }

            // SECURITY FIX: every failure now answers with one identical 401.
            //
            // This used to call GetFailedLoginResponse, which looked the account up and
            // then branched: a registered address came back with its real
            // failedAttemptCount, or with 423 Locked when it was locked out, while an
            // unregistered one came back with a bare 401. Status code and body together
            // told an anonymous caller whether an address had an account -- the exact
            // oracle the comment below this method says was deliberately avoided by not
            // shipping a check-lockout endpoint. It leaked through the login response
            // instead.
            //
            // Attempt counts and lockout state are still reported to the account owner,
            // through the security alert emails LoginCommandHandler sends. Dropping the
            // lookup also removes four database round-trips from every failed login.
            return StatusCode(
                StatusCodes.Status401Unauthorized,
                LoginResponseDto.CreateInvalidCredentials());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login.");
            return StatusCode(StatusCodes.Status500InternalServerError,
                new LoginResponseDto
                {
                    Success = false,
                    StatusCode = 500,
                    Message = "An error occurred during login. Please try again later.",
                    ErrorCode = LoginErrorCodes.ServerError,
                    ErrorType = LoginErrorType.ServerError,
                    RecommendedAction = "contact-support"
                });
        }
    }

    // NOTE: an anonymous GET /auth/check-lockout endpoint was deliberately NOT added.
    // Answering "is this email locked?" without credentials distinguishes registered from
    // unregistered addresses and hands attackers a free user-enumeration oracle.
    //
    // The login response used to carry isAccountLocked/lockoutEndTimeUtc on the theory
    // that whoever submitted the address had already proved they knew it. That was
    // wrong: submitting an address proves nothing, so the login response was the same
    // oracle by a different route. It now answers identically either way, and lockout
    // state is delivered to the account owner's inbox instead.

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

    // ============ Helper Methods ============

    // SECURITY FIX: this used to read X-Forwarded-For / X-Real-IP straight off the
    // request. Those headers are attacker-controlled, so anyone could forge the IP
    // recorded in audit logs, on RefreshToken.CreatedByIp, and in the "sign-in from
    // a new location" security alerts sent to users.
    //
    // The trusted value is already available here: AddTrustedForwardedHeaders
    // configures a KnownProxies/KnownNetworks allowlist (mandatory in Staging and
    // Production) and app.UseForwardedHeaders() has resolved RemoteIpAddress from
    // it before the request reaches this controller. Reading the raw header bypassed
    // that whole trust boundary. This now matches UsersController/PropertiesController.
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

