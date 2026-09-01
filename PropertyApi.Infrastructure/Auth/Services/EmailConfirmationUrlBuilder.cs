using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Infrastructure.Auth.Services;

/// <summary>
/// Builds the address-confirmation link, under the same rules as
/// <see cref="PasswordResetUrlBuilder" />.
///
/// This used to live inline in EmailVerificationService, reading Frontend:BaseUrl with a
/// hard-coded localhost fallback. Neither that key nor its alternate existed in any
/// appsettings file, so every confirmation link ever sent -- Production included --
/// pointed at http://localhost:4200, silently. Failing to start is the correct answer to
/// a missing trusted base URL; sending unusable links is not.
/// </summary>
public sealed class EmailConfirmationUrlBuilder : IEmailConfirmationUrlBuilder
{
    private const string DevelopmentFallbackBaseUrl = "http://localhost:4200";

    private const string ConfirmationPath = "#/auth/verify-email";

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public EmailConfirmationUrlBuilder(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public string Build(
        Guid identityId,
        string token)
    {
        // Deliberately not derived from the request's scheme or Host header: a proxy that
        // forwards an attacker-controlled Host would otherwise mint confirmation links
        // pointing at the attacker's site, with a valid token attached.
        var baseUrl = ResolveConfiguredBaseUrl();

        // The recipient's address used to ride along as a third query parameter. Neither
        // the verify endpoint nor the frontend page reads it, so it was personal data in a
        // URL for no purpose -- it is gone.
        return $"{baseUrl}/{ConfirmationPath}" +
            $"?userId={identityId}" +
            $"&token={Uri.EscapeDataString(token)}";
    }

    private string ResolveConfiguredBaseUrl()
    {
        var configuredUrl = _configuration["Frontend:BaseUrl"];

        if (string.IsNullOrWhiteSpace(configuredUrl))
        {
            if (_environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "Frontend:BaseUrl is required in Production and must point to the trusted frontend origin.");
            }

            return DevelopmentFallbackBaseUrl;
        }

        var baseUrl = configuredUrl.Trim().TrimEnd('/');

        // BUG-26: see PasswordResetUrlBuilder's identical guard for why the scheme must be
        // checked explicitly -- UriKind.Absolute alone accepts a rooted path like
        // "/auth/verify-email" as a valid file:// URI on Linux but rejects it on Windows.
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Frontend:BaseUrl must be an absolute HTTP(S) URL.");
        }

        if (_environment.IsProduction() && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Frontend:BaseUrl must use HTTPS in Production.");
        }

        return baseUrl;
    }
}
