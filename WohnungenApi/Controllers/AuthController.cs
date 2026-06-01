using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using WohnungenApi.Domain.Users.Entities;
using WohnungenApi.Infrastructure.Identity.Entities;
using WohnungenApi.Infrastructure.Persistence;

namespace WohnungenApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AuthController(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        AppDbContext db,
        IConfiguration config)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _config = config;
    }

    // ── POST /api/auth/register ──────────────────────────────
    [HttpPost("register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest dto)
    {
        if (await _userManager.FindByEmailAsync(dto.Email) is not null)
            return BadRequest(new { message = "Email already registered." });

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

        await _userManager.AddToRoleAsync(user, "User");

        return StatusCode(StatusCodes.Status201Created,
            new { message = "Registration successful.", userId = user.Id });
    }

    // ── POST /api/auth/login ─────────────────────────────────
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

        var accessToken = await GenerateAccessTokenAsync(user);
        var refreshToken = CreateRefreshToken(GetClientIp());

        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = refreshToken,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedByIp = GetClientIp()
        });

        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            accessToken,
            refreshToken,
            expiresIn = 3600 // seconds
        });
    }

    // ── POST /api/auth/refresh ───────────────────────────────
    [HttpPost("refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest dto)
    {
        var stored = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == dto.RefreshToken);

        if (stored is null || !stored.IsActive)
            return Unauthorized(new { message = "Invalid or expired refresh token." });

        // Rotate: revoke old, issue new
        stored.IsRevoked = true;
        stored.RevokedAt = DateTime.UtcNow;
        stored.RevokedByIp = GetClientIp();

        var newRefreshToken = CreateRefreshToken(GetClientIp());
        stored.ReplacedByToken = newRefreshToken;

        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = newRefreshToken,
            UserId = stored.UserId,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedByIp = GetClientIp()
        });

        await _db.SaveChangesAsync();

        var newAccessToken = await GenerateAccessTokenAsync(stored.User);

        return Ok(new
        {
            accessToken = newAccessToken,
            refreshToken = newRefreshToken
        });
    }

    // ── POST /api/auth/logout ────────────────────────────────
    [Authorize]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest dto)
    {
        var stored = await _db.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == dto.RefreshToken);

        if (stored is not null && stored.IsActive)
        {
            stored.IsRevoked = true;
            stored.RevokedAt = DateTime.UtcNow;
            stored.RevokedByIp = GetClientIp();
            await _db.SaveChangesAsync();
        }

        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────
    private async Task<string> GenerateAccessTokenAsync(User user)
    {
        var jwtSection = _config.GetSection("Jwt");
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSection["Key"]!));

        var roles = await _userManager.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
            new(ClaimTypes.NameIdentifier,     user.Id.ToString()),
            new("firstName", user.FirstName),
            new("lastName",  user.LastName),
        };

        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            Issuer = jwtSection["Issuer"],
            Audience = jwtSection["Audience"],
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private static string CreateRefreshToken(string? ip)
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }

    private string? GetClientIp()
        => HttpContext.Connection.RemoteIpAddress?.ToString();
}

// ── Request DTOs ─────────────────────────────────────────────
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