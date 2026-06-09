using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Security;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Infrastructure.Identity.Entities;
using PropertyApi.Infrastructure.Identity.Services;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly IEmailSender _emailSender;
    private readonly IConfiguration _configuration;
    private readonly JwtOptions _jwtOptions;

    public AuthController(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        AppDbContext db,
        ITokenService tokenService,
        IEmailSender emailSender,
        IConfiguration configuration,
        IOptions<JwtOptions> jwtOptions)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _tokenService = tokenService;
        _emailSender = emailSender;
        _configuration = configuration;
        _jwtOptions = jwtOptions.Value;
    }

    // -- POST /api/auth/register ------------------------------
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest dto)
    {
        var email = NormalizeEmail(dto.Email);

        if (await _userManager.FindByEmailAsync(email) is not null)
            return Conflict(new { message = "Email already registered." });

        var user = new User
        {
            UserName = email,
            Email = email,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(e => e.Description) });

        var roleResult = await _userManager.AddToRoleAsync(
            user,
            RoleNames.User);

        if (!roleResult.Succeeded)
        {
            var errors = roleResult.Errors.Select(error => error.Description);
            return BadRequest(new { errors });
        }

        return StatusCode(StatusCodes.Status201Created,
            new { message = "Registration successful.", userId = user.Id });
    }

    // -- POST /api/auth/login ---------------------------------
    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest dto)
    {
        var user = await _userManager.FindByEmailAsync(NormalizeEmail(dto.Email));
        if (user is null || user.IsDeleted)
            return Unauthorized(new { message = "Invalid credentials." });

        var result = await _signInManager.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);
        if (!result.Succeeded)
            return Unauthorized(new { message = "Invalid credentials." });

        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _tokenService.GenerateAccessToken(user, roles.ToArray());
        var refreshToken = _tokenService.GenerateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = HashToken(refreshToken),
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(
                _jwtOptions.RefreshTokenDays),
            CreatedByIp = GetClientIp()
        });

        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            accessToken,
            refreshToken,
            expiresIn = _jwtOptions.AccessTokenMinutes * 60
        });
    }

    // -- POST /api/auth/forgot-password ------------------------
    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth-password-reset")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword(
    [FromBody] ForgotPasswordRequest dto,
    CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Email))
            return BadRequest(new { message = "Email is required." });

        var email = NormalizeEmail(dto.Email);
        var user = await _userManager.FindByEmailAsync(email);

        if (user is not null && !user.IsDeleted && !string.IsNullOrWhiteSpace(user.Email))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetUrl = BuildResetPasswordUrl(email, token);
            var emailBody = BuildPasswordResetEmailBody(user, resetUrl);

            await _emailSender.SendEmailAsync(
                user.Email!,
                "Reset your password",
                emailBody);
        }

        // Always return the same response to prevent account enumeration.
        return Ok(new
        {
            message = "If the email is registered, a password reset link has been sent."
        });
    }

    // -- POST /api/auth/reset-password -------------------------
    [HttpPost("reset-password")]
    [EnableRateLimiting("auth-password-reset")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest dto,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrWhiteSpace(dto.Token) ||
            string.IsNullOrWhiteSpace(dto.NewPassword))
        {
            return BadRequest(new { message = "Email, token, and new password are required." });
        }

        if (!string.Equals(dto.NewPassword, dto.ConfirmPassword, StringComparison.Ordinal))
            return BadRequest(new { message = "New password and confirmation password do not match." });

        var email = NormalizeEmail(dto.Email);
        var user = await _userManager.FindByEmailAsync(email);

        if (user is null || user.IsDeleted)
            return BadRequest(new { message = "Invalid password reset request." });

        var isSamePassword = await _userManager.CheckPasswordAsync(user, dto.NewPassword);
        if (isSamePassword)
            return Conflict(new { message = "New password must be different from the current password." });

        var token = Uri.UnescapeDataString(dto.Token);
        var result = await _userManager.ResetPasswordAsync(user, token, dto.NewPassword);

        if (!result.Succeeded)
            return BadRequest(new { errors = result.Errors.Select(error => error.Description) });

        await _userManager.UpdateSecurityStampAsync(user);
        await RevokeActiveRefreshTokensAsync(user.Id, ct);

        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(new { message = "Password has been reset successfully." });
    }

    // -- POST /api/auth/refresh -------------------------------
    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest dto,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var oldTokenHash = HashToken(dto.RefreshToken);
        var clientIp = GetClientIp();

        await using var transaction =
            await _db.Database.BeginTransactionAsync(ct);

        var stored = await _db.RefreshTokens
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(token => token.User)
            .SingleOrDefaultAsync(
                token => token.TokenHash == oldTokenHash,
                ct);

        if (stored is null ||
            stored.IsRevoked ||
            stored.ExpiresAt <= now ||
            stored.User is null ||
            stored.User.IsDeleted)
        {
            return Unauthorized(new
            {
                message = "Invalid or expired refresh token."
            });
        }

        var newRefreshToken =
            _tokenService.GenerateRefreshToken();

        var newRefreshTokenHash =
            HashToken(newRefreshToken);

        var affectedRows = await _db.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token =>
                token.Id == stored.Id &&
                !token.IsRevoked &&
                token.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        token => token.IsRevoked,
                        true)
                    .SetProperty(
                        token => token.RevokedAt,
                        now)
                    .SetProperty(
                        token => token.RevokedByIp,
                        clientIp)
                    .SetProperty(
                        token => token.ReplacedByTokenHash,
                        newRefreshTokenHash),
                ct);

        if (affectedRows != 1)
        {
            await transaction.RollbackAsync(ct);

            return Unauthorized(new
            {
                message = "Refresh token was already used."
            });
        }

        _db.RefreshTokens.Add(new RefreshToken
        {
            TokenHash = newRefreshTokenHash,
            UserId = stored.UserId,
            ExpiresAt = now.AddDays(
                _jwtOptions.RefreshTokenDays),
            CreatedByIp = clientIp
        });

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var roles = await _userManager
            .GetRolesAsync(stored.User);

        var accessToken =
            _tokenService.GenerateAccessToken(
                stored.User,
                roles.ToArray());

        return Ok(new
        {
            accessToken,
            refreshToken = newRefreshToken,
            expiresIn = _jwtOptions.AccessTokenMinutes * 60
        });
    }

    // -- POST /api/auth/logout --------------------------------
    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest dto)
    {
        var userIdText = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdText, out var userId))
            return Unauthorized();

        var tokenHash = HashToken(dto.RefreshToken);
        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(
                token =>
                    token.TokenHash == tokenHash &&
                    token.UserId == userId);

        if (stored is not null && stored.IsActive)
        {
            stored.IsRevoked = true;
            stored.RevokedAt = DateTime.UtcNow;
            stored.RevokedByIp = GetClientIp();
            await _db.SaveChangesAsync();
        }

        return NoContent();
    }

    private async Task RevokeActiveRefreshTokensAsync(
        Guid userId,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var clientIp = GetClientIp();

        await _db.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token =>
                token.UserId == userId &&
                !token.IsRevoked &&
                token.ExpiresAt > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(token => token.IsRevoked, true)
                    .SetProperty(token => token.RevokedAt, now)
                    .SetProperty(token => token.RevokedByIp, clientIp),
                ct);
    }

    private string BuildResetPasswordUrl(string email, string token)
    {
        var configuredUrl = _configuration["Frontend:PasswordResetUrl"];
        var baseUrl = !string.IsNullOrWhiteSpace(configuredUrl)
            ? configuredUrl.TrimEnd('?')
            : $"{Request.Scheme}://{Request.Host}/reset-password";

        return baseUrl.Contains('?', StringComparison.Ordinal)
            ? $"{baseUrl}&email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}"
            : $"{baseUrl}?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }

    private static string BuildPasswordResetEmailBody(User user, string resetUrl)
    {
        var name = !string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.DisplayName
            : $"{user.FirstName} {user.LastName}".Trim();

        if (string.IsNullOrWhiteSpace(name))
            name = "there";

        return $"""
            <html>
            <body style="font-family:Arial,sans-serif;line-height:1.6">
                <p>Hello {WebUtility.HtmlEncode(name)},</p>
                <p>We received a request to reset your PropertyApi password.</p>
                <p><a href="{WebUtility.HtmlEncode(resetUrl)}">Reset your password</a></p>
                <p>If you did not request this, you can safely ignore this email.</p>
            </body>
            </html>
            """;
    }

    private string? GetClientIp()
        => HttpContext.Connection.RemoteIpAddress?.ToString();

    private static string NormalizeEmail(string email)
        => email.Trim().ToLowerInvariant();

    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}

// -- Request DTOs ---------------------------------------------
public sealed record RegisterRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password);

public sealed record LoginRequest(
    string Email,
    string Password);

public sealed record RefreshRequest(
    string RefreshToken);

public sealed record ForgotPasswordRequest(
    string Email);

public sealed record ResetPasswordRequest(
    string Email,
    string Token,
    string NewPassword,
    string ConfirmPassword);
