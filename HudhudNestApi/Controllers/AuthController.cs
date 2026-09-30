using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Auth.Commands.ForgotPassword;
using HudhudNestApi.Application.Auth.Commands.Login;
using HudhudNestApi.Application.Auth.Commands.Logout;
using HudhudNestApi.Application.Auth.Commands.RefreshToken;
using HudhudNestApi.Application.Auth.Commands.Register;
using HudhudNestApi.Application.Auth.Commands.ResetPassword;
using HudhudNestApi.Application.Auth.Commands.SocialLogin;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Security;
using HudhudNestApi.Security.Auth;

namespace HudhudNestApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
// Auth responses carry tokens, challenge ids and account status: never stored by a browser or a proxy.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController : ControllerBase
{
    private readonly ISender _sender;
    private readonly IJwtTokenSettings _jwtSettings;
    private readonly ILogger<AuthController> _logger;

    // ILoginIdentityService was injected only so the login failure path could look the
    // account up and report its attempt count and lockout state. That lookup is gone
    // (see the Login method), and with it the controller's reason to reach past MediatR
    // into the identity store at all.
    public AuthController(
        ISender sender,
        IJwtTokenSettings jwtSettings,
        ILogger<AuthController> logger)
    {
        _sender = sender;
        _jwtSettings = jwtSettings;
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
            Password: dto.Password,
            PrivacyPolicyAccepted: dto.PrivacyPolicyAccepted,
            PrivacyPolicyVersion: dto.PrivacyPolicyVersion,
            ConsentSource: dto.ConsentSource), ct);

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
                    PiiMasking.MaskIp(ipAddress));

                // RELEASE-BLOCKERS-AR.md B-13: the refresh token now travels only in an
                // HttpOnly cookie, never in a body a script on the page could read.
                // LoginResponseDto.CreateSuccess is kept as-is (see LoginResponseContractTests)
                // — its RefreshToken member is deliberately not set here rather than removed
                // from the DTO, since other callers of that factory are unaffected by this
                // endpoint's choice to withhold it.
                RefreshTokenCookie.Attach(
                    Response,
                    result.RefreshToken,
                    _jwtSettings.RefreshTokenDays);

                var response = new LoginResponseDto
                {
                    Success = true,
                    StatusCode = 200,
                    Message = "Login successful",
                    AccessToken = result.AccessToken,
                    RefreshToken = null,
                    ExpiresIn = result.ExpiresIn
                };

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
        [FromBody] RefreshRequest? dto,
        CancellationToken ct)
    {
        // RELEASE-BLOCKERS-AR.md B-13: the token a real browser client presents now arrives
        // only via the refresh_token cookie — dto/dto.RefreshToken exists purely so a
        // transitional or non-browser caller (internal tooling, a mobile client not yet
        // verified against the cookie flow) can still assert an explicit value. See
        // RefreshTokenCookie.Read's doc comment for why the explicit value wins when present.
        var refreshToken = RefreshTokenCookie.Read(Request, dto?.RefreshToken);

        if (refreshToken is null)
            return Unauthorized(new { message = "No refresh token was supplied." });

        var result = await _sender.Send(new RefreshTokenCommand(
            RefreshToken: refreshToken,
            IpAddress: GetClientIp()), ct);

        if (!result.Success)
            return Unauthorized(new { message = result.Message, errorCode = result.ErrorCode });

        RefreshTokenCookie.Attach(Response, result.RefreshToken, _jwtSettings.RefreshTokenDays);

        return Ok(new
        {
            accessToken = result.AccessToken,
            expiresIn = result.ExpiresIn
        });
    }

    // POST /api/auth/logout
    //
    // Exempt from CookieCsrfProtectionMiddleware on purpose. The middleware exists for
    // requests whose only credential is the ambient refresh_token cookie; this endpoint also
    // requires a valid Bearer access token ([Authorize]), which a cross-site page can neither
    // read nor attach, so there is no CSRF exposure left to protect. Enforcing it here only
    // broke real clients: antiforgery request tokens are bound to the authenticated user, and
    // the SPA (like the Staging smoke) fetches its token once, before sign-in, so every logout
    // that carried the refresh cookie was rejected with 403 CSRF_VALIDATION_FAILED and the
    // refresh token was never revoked server-side. POST /api/auth/refresh keeps full CSRF
    // protection (it is anonymous and cookie-driven).
    [Authorize]
    [IgnoreAntiforgeryToken]
    [HttpPost("logout")]
    [EnableRateLimiting("auth-logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(
        [FromBody] RefreshRequest? dto,
        CancellationToken ct)
    {
        var userIdText = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdText, out var userId))
            return Unauthorized();

        var refreshToken = RefreshTokenCookie.Read(Request, dto?.RefreshToken);

        if (refreshToken is not null)
        {
            await _sender.Send(new LogoutCommand(
                UserId: userId,
                RefreshToken: refreshToken,
                IpAddress: GetClientIp()), ct);
        }

        // Clear unconditionally: an already-expired/missing cookie costs nothing to clear
        // again, and the browser's session must not keep offering a token client-side has
        // no way to inspect or know is now meaningless.
        RefreshTokenCookie.Clear(Response);

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

        RefreshTokenCookie.Attach(Response, result.RefreshToken, _jwtSettings.RefreshTokenDays);

        return Ok(new
        {
            accessToken = result.AccessToken,
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
            AppleNonce: dto.Nonce,
            IpAddress: GetClientIp()), ct);

        if (!result.Success)
            return BadRequest(new { message = result.Message });

        RefreshTokenCookie.Attach(Response, result.RefreshToken, _jwtSettings.RefreshTokenDays);

        return Ok(new
        {
            accessToken = result.AccessToken,
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
//
// PrivacyPolicyAccepted/PrivacyPolicyVersion/ConsentSource are additive and optional
// (docs/privacy/privacy-gaps.md, P1) -- an older client build that predates the consent
// checkbox simply omits them, and registration proceeds exactly as it did before this
// change, just without a ConsentRecord (see RegisterCommand's doc comment).
public sealed record RegisterRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    bool PrivacyPolicyAccepted = false,
    string? PrivacyPolicyVersion = null,
    string? ConsentSource = null);

public sealed record LoginRequest(
    string Email,
    string Password);

public sealed record GoogleLoginRequest(
    string IdToken);

public sealed record AppleLoginRequest(
    string IdentityToken,
    string? AuthorizationCode,
    string? FirstName,
    string? LastName,
    // Raw (unhashed) nonce the client passed to AppleID.auth.init — see B-16 in
    // RELEASE-BLOCKERS-AR.md. Optional at the DTO level so a malformed/old request still
    // deserializes; AppleTokenVerifier is what actually enforces it once the frontend sends it.
    string? Nonce = null);
// Optional now (RELEASE-BLOCKERS-AR.md B-13): a real browser client sends no body at all —
// the refresh_token cookie carries the value. RefreshToken remains so a transitional or
// non-browser caller (internal tooling, a mobile client pending its own cookie verification)
// can still assert an explicit token; see RefreshTokenCookie.Read.
public sealed record RefreshRequest(
    string? RefreshToken = null);

public sealed record ForgotPasswordRequest(
    string Email);

public sealed record ResetPasswordRequest(
    string Email,
    string Token,
    string NewPassword,
    string ConfirmPassword);

