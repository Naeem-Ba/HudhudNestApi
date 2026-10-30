using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace HudhudNestApi.Security;

/// <summary>
/// Launch audit A4-001. The web app sends every <c>/api/auth/*</c> call through a Netlify Edge
/// Function (same-origin cookies), so the API sees a Netlify address as the client and the per-IP
/// rate limits (login, register, OTP, refresh, password reset) became one bucket for every web
/// user. The edge function now adds the visitor's IP in <c>X-HN-Client-IP</c> together with a
/// shared secret in <c>X-HN-Edge-Secret</c>. This middleware trusts the IP only when the secret
/// matches <c>EdgeProxy:SharedSecret</c> (constant-time compare), and always strips both headers so
/// nothing downstream can read a spoofed value. With no secret configured it changes nothing.
/// </summary>
public sealed class EdgeClientIpMiddleware
{
    public const string ClientIpHeader = "X-HN-Client-IP";
    public const string EdgeSecretHeader = "X-HN-Edge-Secret";

    /// <summary>Shorter secrets are ignored: guessing one must not be practical.</summary>
    public const int MinimumSecretLength = 32;

    private readonly RequestDelegate _next;
    private readonly byte[]? _secret;

    public EdgeClientIpMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<EdgeClientIpMiddleware> logger)
    {
        _next = next;

        var secret = configuration["EdgeProxy:SharedSecret"]?.Trim();
        if (string.IsNullOrEmpty(secret))
        {
            return;
        }

        if (secret.Length < MinimumSecretLength)
        {
            logger.LogWarning(
                "EdgeProxy:SharedSecret is shorter than {Length} characters and is ignored; the auth proxy client IP will not be trusted.",
                MinimumSecretLength);
            return;
        }

        _secret = Encoding.UTF8.GetBytes(secret);
    }

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Request.Headers;
        var claimedIp = headers[ClientIpHeader].ToString();
        var presentedSecret = headers[EdgeSecretHeader].ToString();

        headers.Remove(ClientIpHeader);
        headers.Remove(EdgeSecretHeader);

        if (_secret is not null &&
            presentedSecret.Length > 0 &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presentedSecret), _secret) &&
            IPAddress.TryParse(claimedIp, out var clientIp))
        {
            context.Connection.RemoteIpAddress = clientIp;
        }

        return _next(context);
    }
}
