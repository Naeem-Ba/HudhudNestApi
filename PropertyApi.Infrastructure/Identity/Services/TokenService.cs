using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Application.Common.Security;

namespace PropertyApi.Infrastructure.Identity.Services;

/// <summary>
/// TODO: تنفيذ خدمة Auth المتقدمة (Refresh Token rotation, Revocation, etc.)
/// حالياً: Auth مُعالَجة عبر AuthController مباشرة مع RegisterUserCommandHandler.
/// هذا الكلاس مخصص للمرحلة التالية.
/// </summary>
public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _jwtOptions;

    public TokenService(IOptions<JwtOptions> jwtOptions)
    {
        _jwtOptions = jwtOptions.Value;
    }

    public string GenerateAccessToken(User user, IReadOnlyCollection<string> roles)
    {
        var claims = new List<Claim>
{
    new(ClaimTypes.NameIdentifier, user.Id.ToString()),
    new(ClaimTypes.Email, user.Email ?? string.Empty),
    new(ClaimTypes.Name, user.UserName ?? user.Email ?? string.Empty),
    new(
        CustomClaimTypes.SecurityStamp,
        user.SecurityStamp ?? string.Empty)
};

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(randomBytes);
    }
}
