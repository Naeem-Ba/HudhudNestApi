using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropertyApi.Application.Auth.Contracts;
using PropertyApi.Infrastructure.Settings;

namespace PropertyApi.Infrastructure.Auth;

internal sealed class GoogleTokenVerifier : ISocialTokenVerifier
{
    // Production incident (2026-09-18): a real Google Sign-In attempt got an opaque 500 from
    // the Netlify edge proxy in front of this API (netlify/edge-functions/auth-proxy.mjs in
    // the frontend repo), not the JSON 400 this method normally produces for a bad token.
    // Root cause traced here: GoogleJsonWebSignature.ValidateAsync(string, ValidationSettings)
    // has no CancellationToken overload at all, and its own doc comment says Google's certs
    // are fetched live over the network on a cache miss (cached only "once per hour") --  so a
    // slow/blocked network path to Google, most likely right after a fresh deploy when the
    // process-local cert cache is cold, could leave this call hanging indefinitely. That
    // comfortably outlasts the edge proxy's own request budget, which then gets killed by the
    // platform itself -- bypassing this app's own exception middleware and the proxy's own
    // catch block alike, producing exactly the unhelpful bare 500 observed live. This timeout
    // bounds the wait from our side (the one thing we *can* control without the library's
    // cooperation) so a slow Google call fails the same safe way an actually-invalid token
    // does -- a clean, fast 400 -- instead of holding the request open until something
    // upstream gives up ungracefully. Deliberately short: this only ever matters on a cache
    // miss, and a legitimate Google cert fetch normally completes in well under a second.
    private static readonly TimeSpan VerificationTimeout = TimeSpan.FromSeconds(10);

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
            var validationTask = GoogleJsonWebSignature.ValidateAsync(
                token,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { _settings.GoogleClientId }
                });

            var timeoutTask = Task.Delay(VerificationTimeout, ct);
            var completed = await Task.WhenAny(validationTask, timeoutTask);

            if (completed == timeoutTask)
            {
                _logger.LogWarning(
                    "Google identity token validation timed out after {TimeoutSeconds}s " +
                    "(likely a cold certificate-cache network fetch to Google) -- rejecting " +
                    "the token rather than leaving the request pending.",
                    VerificationTimeout.TotalSeconds);
                return null;
            }

            var payload = await validationTask;

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
