using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using PropertyApi.Application.SocialDistribution.Interfaces;

namespace PropertyApi.Infrastructure.SocialDistribution;

/// <summary>
/// Reads the same <c>Frontend:BaseUrl</c> configuration PasswordResetUrlBuilder/
/// EmailConfirmationUrlBuilder already use, appends <c>/properties/{id}</c> (this backend's
/// well-known canonical property route — same one <c>PropertyShareUrlService</c> targets on the
/// frontend), and attaches the 4 UTM params via <see cref="Uri"/>/query-string APIs. Never
/// touches — and is never called by — anything that builds the canonical/OG URL.
/// </summary>
public sealed class SocialDistributionTargetUrlBuilder : ISocialDistributionTargetUrlBuilder
{
    private const string DevelopmentFallbackBaseUrl = "http://localhost:4200";

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public SocialDistributionTargetUrlBuilder(IConfiguration configuration, IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public string BuildAttributedTargetUrl(
        Guid propertyId,
        string utmSource,
        string utmMedium,
        string utmCampaign,
        string utmContent)
    {
        var baseUrl = ResolveConfiguredBaseUrl();

        var uriBuilder = new UriBuilder(baseUrl)
        {
            Path = $"/properties/{propertyId}",
        };

        var query = new List<string>
        {
            $"utm_source={Uri.EscapeDataString(utmSource)}",
            $"utm_medium={Uri.EscapeDataString(utmMedium)}",
            $"utm_campaign={Uri.EscapeDataString(utmCampaign)}",
            $"utm_content={Uri.EscapeDataString(utmContent)}",
        };

        uriBuilder.Query = string.Join('&', query);

        return uriBuilder.Uri.ToString();
    }

    private string ResolveConfiguredBaseUrl()
    {
        var configuredUrl = _configuration["Frontend:BaseUrl"];

        if (string.IsNullOrWhiteSpace(configuredUrl))
        {
            if (_environment.IsProduction())
            {
                throw new InvalidOperationException(
                    "Frontend:BaseUrl is required in Production to build Social Distribution target URLs.");
            }

            return DevelopmentFallbackBaseUrl;
        }

        var baseUrl = configuredUrl.Trim().TrimEnd('/');

        // Same Windows/Linux UriKind.Absolute pitfall documented in PasswordResetUrlBuilder
        // (BUG-26) — require a real http(s) scheme explicitly.
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Frontend:BaseUrl must be an absolute HTTP(S) URL.");
        }

        if (_environment.IsProduction() && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Frontend:BaseUrl must use HTTPS in Production.");
        }

        return baseUrl;
    }
}
