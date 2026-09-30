using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using HudhudNestApi.Application.Auth.Contracts;
using HudhudNestApi.Infrastructure.Settings;

namespace HudhudNestApi.Infrastructure.Auth;

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

    public async Task<SocialUserInfo?> VerifyAsync(
        string token,
        string? expectedNonce = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.AppleClientId))
        {
            _logger.LogWarning("SocialAuth:AppleClientId is not configured.");
            return null;
        }

        try
        {
            var signingKeys = await GetApplePublicKeysAsync(ct);

            // MapInboundClaims = false is load-bearing, not stylistic: JwtSecurityTokenHandler
            // silently rewrites short claim names to long legacy URIs by default (e.g. "sub"
            // becomes ".../claims/nameidentifier", "email" becomes ".../claims/emailaddress").
            // With the default left on, every principal.FindFirst("sub") below returns null —
            // Apple sign-in would validate the token successfully and then fail every single
            // login with no error logged, because the sub-not-found branch returns null
            // silently by design (an actually-missing sub is not itself unusual for a
            // malformed token). Found while adding nonce enforcement for B-16 in
            // RELEASE-BLOCKERS-AR.md — a real, signed test token was the only thing that
            // surfaced it; nothing before this exercised this method with a real JWT.
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
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

            // B-16 (RELEASE-BLOCKERS-AR.md): without this, a valid Apple identity token
            // obtained for one sign-in attempt could be replayed against this endpoint from
            // any other session within its validity window. The token's own signature only
            // proves Apple issued it, not that it was issued for *this* request. Only
            // enforced once a caller actually supplies a nonce — the frontend and backend
            // ship this together, so no partial-rollout state where one side has it and the
            // other does not is expected to reach Production.
            if (!string.IsNullOrWhiteSpace(expectedNonce))
            {
                var nonceClaim = principal.FindFirst("nonce")?.Value;
                var expectedNonceHash = HashNonce(expectedNonce);

                if (string.IsNullOrWhiteSpace(nonceClaim) ||
                    !string.Equals(nonceClaim, expectedNonceHash, StringComparison.Ordinal))
                {
                    _logger.LogWarning("Apple identity token nonce did not match the expected value.");
                    return null;
                }
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

    /// <summary>
    /// Apple's identity token carries the SHA-256 hash of the raw nonce the client passed to
    /// AppleID.auth.init, hex-encoded lowercase — not the raw value itself. Lowercase
    /// specifically: unlike RefreshTokenStore.HashToken elsewhere in this codebase (which
    /// uses Convert.ToHexString's uppercase output for an internal-only comparison), this
    /// hash is compared against a claim whose casing this codebase does not control.
    /// </summary>
    private static string HashNonce(string rawNonce)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawNonce));
        return Convert.ToHexString(hash).ToLowerInvariant();
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
