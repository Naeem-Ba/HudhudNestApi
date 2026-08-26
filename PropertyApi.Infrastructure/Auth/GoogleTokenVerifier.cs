using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropertyApi.Application.Auth.Contracts;
using PropertyApi.Infrastructure.Settings;

namespace PropertyApi.Infrastructure.Auth;

internal sealed class GoogleTokenVerifier : ISocialTokenVerifier
{
    private readonly SocialAuthSettings _settings;
    private readonly ILogger<GoogleTokenVerifier> _logger;

    public GoogleTokenVerifier(
        IOptions<SocialAuthSettings> settings,
        ILogger<GoogleTokenVerifier> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public string ProviderName => "Google";

    // expectedNonce is unused here: Google sign-in in this codebase goes through Google
    // Identity Services (One Tap), which does not thread a client-generated nonce through
    // the ID token today. B-16 (RELEASE-BLOCKERS-AR.md) scopes the nonce fix to Apple only.
    public async Task<SocialUserInfo?> VerifyAsync(
        string token,
        string? expectedNonce = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.GoogleClientId))
        {
            _logger.LogWarning("SocialAuth:GoogleClientId is not configured.");
            return null;
        }

        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(
                token,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _settings.GoogleClientId }
                });

            return new SocialUserInfo(
                ProviderId: payload.Subject,
                ProviderName: ProviderName,
                Email: payload.Email,
                IsEmailVerified: payload.EmailVerified,
                FirstName: payload.GivenName,
                LastName: payload.FamilyName,
                AvatarUrl: payload.Picture);
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Google identity token is invalid.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Google identity token validation failed.");
            return null;
        }
    }
}
