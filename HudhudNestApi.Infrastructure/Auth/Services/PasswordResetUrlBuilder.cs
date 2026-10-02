using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using HudhudNestApi.Application.Auth.Interfaces;

namespace HudhudNestApi.Infrastructure.Auth.Services;

public sealed class PasswordResetUrlBuilder : IPasswordResetUrlBuilder
{
    private const string DevelopmentFallbackResetUrl = "http://localhost:4200/auth/reset-password";

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public PasswordResetUrlBuilder(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public string Build(
        string email,
        string token,
        string? requestScheme,
        string? requestHost)
    {
        // Do not build security-sensitive links from request Host headers.
        // Host-header fallback can lead to password reset link poisoning behind proxies.
        _ = requestScheme;
        _ = requestHost;

        var baseUrl = ResolveConfiguredBaseUrl();
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";

        return $"{baseUrl}{separator}email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }

    private string ResolveConfiguredBaseUrl()
    {
        var configuredUrl = _configuration["Frontend:PasswordResetUrl"];

        if (string.IsNullOrWhiteSpace(configuredUrl))
        {
            if (_environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "Frontend:PasswordResetUrl is required in Production and must point to the trusted frontend reset-password page.");
            }

            return DevelopmentFallbackResetUrl;
        }

        var baseUrl = configuredUrl.Trim().TrimEnd('?', '&');

        // BUG-26: Uri.TryCreate(..., UriKind.Absolute, ...) parses a rooted path such as
        // "/auth/reset-password" as a valid absolute file:// URI on Linux (it is a legal
        // absolute filesystem path there) but rejects it on Windows -- so this check alone
        // caught misconfiguration locally and in every Windows test run, while silently
        // accepting it (and minting a file:///... reset link) on the Linux CI/production
        // runtime. Require an actual http(s) scheme, matching the same guard already used
        // for Email:Resend:BaseUrl and the Cloudinary URL elsewhere in this codebase.
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Frontend:PasswordResetUrl must be an absolute HTTP(S) URL.");
        }

        if (_environment.IsProduction() && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Frontend:PasswordResetUrl must use HTTPS in Production.");
        }

        return baseUrl;
    }
}
