using Microsoft.Extensions.Configuration;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Infrastructure.Auth.Services;

public sealed class PasswordResetUrlBuilder : IPasswordResetUrlBuilder
{
    private readonly IConfiguration _configuration;

    public PasswordResetUrlBuilder(IConfiguration configuration)
        => _configuration = configuration;

    public string Build(
        string email,
        string token,
        string? requestScheme,
        string? requestHost)
    {
        var configuredUrl = _configuration["Frontend:PasswordResetUrl"];

        var baseUrl = !string.IsNullOrWhiteSpace(configuredUrl)
            ? configuredUrl.TrimEnd('?')
            : BuildFallbackUrl(requestScheme, requestHost);

        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";

        return $"{baseUrl}{separator}email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }

    private static string BuildFallbackUrl(string? requestScheme, string? requestHost)
    {
        var scheme = string.IsNullOrWhiteSpace(requestScheme) ? "https" : requestScheme;
        var host = string.IsNullOrWhiteSpace(requestHost) ? "localhost" : requestHost;
        return $"{scheme}://{host}/reset-password";
    }
}
