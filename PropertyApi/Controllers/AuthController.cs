using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Infrastructure.Identity.Entities;
using PropertyApi.Infrastructure.Persistence;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Application.Common.Security;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Infrastructure.Identity.Services;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Security.Claims;

namespace PropertyApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly JwtOptions _jwtOptions;

    public AuthController(
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    AppDbContext db,
    ITokenService tokenService,
    IOptions<JwtOptions> jwtOptions)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _tokenService = tokenService;
        _jwtOptions = jwtOptions.Value;
    }

    // -- POST /api/auth/register ------------------------------
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest dto)
    {
        if (await _userManager.FindByEmailAsync(dto.Email) is not null)
            return Conflict(new { message = "Email already registered." });

        var user = new User
        {
            UserName = dto.Email.ToLowerInvariant(),
            Email = dto.Email.ToLowerInvariant(),
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
        var user = await _userManager.FindByEmailAsync(dto.Email);
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

    private string? GetClientIp()
        => HttpContext.Connection.RemoteIpAddress?.ToString();
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
