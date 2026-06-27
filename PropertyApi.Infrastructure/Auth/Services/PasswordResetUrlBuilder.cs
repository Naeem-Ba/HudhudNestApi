using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Infrastructure.Auth.Services;

public sealed class PasswordResetUrlBuilder : IPasswordResetUrlBuilder
{
    private const string DevelopmentFallbackResetUrl = "http://localhost:4200/reset-password";

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

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException(
                "Frontend:PasswordResetUrl must be an absolute URL.");
        }

        if (_environment.IsProduction() && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Frontend:PasswordResetUrl must use HTTPS in Production.");
        }

        return baseUrl;
    }
}
