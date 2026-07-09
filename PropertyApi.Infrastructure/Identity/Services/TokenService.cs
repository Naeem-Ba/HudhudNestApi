using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Security;

namespace PropertyApi.Infrastructure.Identity.Services;

public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _jwtOptions;

    public TokenService(
        IOptions<JwtOptions> jwtOptions)
    {
        _jwtOptions = jwtOptions.Value;
    }

    public string GenerateAccessToken(
        AccessTokenSubject subject,
        IReadOnlyCollection<string> roles)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(roles);

        var claims = new List<Claim>
        {
            new(
                ClaimTypes.NameIdentifier,
                subject.IdentityId.ToString()),

            new(
                ClaimTypes.Email,
                subject.Email ?? string.Empty),

            new(
                ClaimTypes.Name,
                subject.UserName
                ?? subject.Email
                ?? string.Empty),

            new(
                CustomClaimTypes.SecurityStamp,
                subject.SecurityStamp
                ?? string.Empty)
        };

        foreach (var role in roles)
        {
            claims.Add(
                new Claim(
                    ClaimTypes.Role,
                    role));
        }

        var key =
            new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    _jwtOptions.Key));

        var credentials =
            new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256);

        var token =
            new JwtSecurityToken(
                issuer: _jwtOptions.Issuer,
                audience: _jwtOptions.Audience,
                claims: claims,
                expires:
                    GetAccessTokenExpiresAtUtc(),
                signingCredentials:
                    credentials);

        return new JwtSecurityTokenHandler()
            .WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes =
            RandomNumberGenerator.GetBytes(64);

        return Convert.ToBase64String(
            randomBytes);
    }

    public DateTime GetAccessTokenExpiresAtUtc()
        => DateTime.UtcNow.AddMinutes(
            _jwtOptions.AccessTokenMinutes);
}