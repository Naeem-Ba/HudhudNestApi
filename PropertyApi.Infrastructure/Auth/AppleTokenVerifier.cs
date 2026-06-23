using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PropertyApi.Application.Auth.Contracts;
using PropertyApi.Infrastructure.Settings;

namespace PropertyApi.Infrastructure.Auth;

internal sealed class AppleTokenVerifier : ISocialTokenVerifier
{
    private const string AppleKeysUrl = "https://appleid.apple.com/auth/keys";
    private const string AppleIssuer = "https://appleid.apple.com";
    private const string CacheKey = "apple_jwks_security_keys";

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly SocialAuthSettings _settings;
    private readonly ILogger<AppleTokenVerifier> _logger;

    public AppleTokenVerifier(
        HttpClient httpClient,
        IMemoryCache cache,
        IOptions<SocialAuthSettings> settings,
        ILogger<AppleTokenVerifier> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _settings = settings.Value;
        _logger = logger;
    }

    public string ProviderName => "Apple";

    public async Task<SocialUserInfo?> VerifyAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.AppleClientId))
        {
            _logger.LogWarning("SocialAuth:AppleClientId is not configured.");
            return null;
        }

        try
        {
            var signingKeys = await GetApplePublicKeysAsync(ct);

            var handler = new JwtSecurityTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = AppleIssuer,
                ValidateAudience = true,
                ValidAudience = _settings.AppleClientId,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = signingKeys,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            var principal = handler.ValidateToken(
                token,
                validationParameters,
                out _);

            var sub = principal.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(sub))
            {
                return null;
            }

            var email = principal.FindFirst("email")?.Value;
            var emailVerified = principal.FindFirst("email_verified")?.Value;

            return new SocialUserInfo(
                ProviderId: sub,
                ProviderName: ProviderName,
                Email: email,
                IsEmailVerified: string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase),
                FirstName: null,
                LastName: null,
                AvatarUrl: null);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Apple identity token is invalid.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Apple identity token validation failed.");
            return null;
        }
    }

    private async Task<IEnumerable<SecurityKey>> GetApplePublicKeysAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out IEnumerable<SecurityKey>? cachedKeys) && cachedKeys is not null)
        {
            return cachedKeys;
        }

        var json = await _httpClient.GetStringAsync(AppleKeysUrl, ct);
        using var jwks = JsonDocument.Parse(json);
        var keys = new List<SecurityKey>();

        foreach (var key in jwks.RootElement.GetProperty("keys").EnumerateArray())
        {
            var rsa = System.Security.Cryptography.RSA.Create();
            rsa.ImportParameters(new System.Security.Cryptography.RSAParameters
            {
                Exponent = Base64UrlDecode(key.GetProperty("e").GetString()!),
                Modulus = Base64UrlDecode(key.GetProperty("n").GetString()!)
            });

            keys.Add(new RsaSecurityKey(rsa)
            {
                KeyId = key.GetProperty("kid").GetString()
            });
        }

        _cache.Set(CacheKey, (IEnumerable<SecurityKey>)keys, TimeSpan.FromHours(1));
        return keys;
    }

    private static byte[] Base64UrlDecode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("Base64Url value is empty.");
        }

        var base64 = value
            .Replace('-', '+')
            .Replace('_', '/');

        var padding = base64.Length % 4;

        if (padding == 2)
        {
            base64 += "==";
        }
        else if (padding == 3)
        {
            base64 += "=";
        }
        else if (padding != 0)
        {
            throw new FormatException("Invalid Base64Url string length.");
        }

        return Convert.FromBase64String(base64);
    }
}
