using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using WohnungenApi.Models;

namespace WohnungenApi.Services
{
    public class JwtService
    {
        private readonly JwtOptions _options;
        private readonly UserManager<Benutzer> _userManager;

        public JwtService(
            IOptions<JwtOptions> options,
            UserManager<Benutzer> userManager)
        {
            _options = options.Value;
            _userManager = userManager;
        }

        // ==========================
        // توليد Access Token
        // ==========================
        public async Task<string> GenerateAccessToken(Benutzer user)
        {
            var roles = await _userManager.GetRolesAsync(user);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email!)
            };

            foreach (var role in roles)
                claims.Add(new Claim(ClaimTypes.Role, role));

            var key = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_options.Key)
            );

            var creds = new SigningCredentials(
                key,
                SecurityAlgorithms.HmacSha256
            );

            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(30), // قصير العمر
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // ==========================
        // توليد Refresh Token عشوائي آمن
        // ==========================
        public string GenerateRefreshToken()
        {
            var randomBytes = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            return Convert.ToBase64String(randomBytes);
        }
    }
}