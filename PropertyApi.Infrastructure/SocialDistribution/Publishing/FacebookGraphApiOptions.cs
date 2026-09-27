namespace PropertyApi.Infrastructure.SocialDistribution.Publishing;

/// <summary>
/// SocialDistribution:Facebook — credentials and transport settings for the real Facebook Graph
/// API integration. A Page access token (from a Facebook App with <c>pages_manage_posts</c> +
/// <c>pages_read_engagement</c> granted, generated for the specific Page this account publishes
/// to) — never a user access token, and never the App Secret itself.
/// </summary>
public sealed class FacebookGraphApiOptions
{
    public const string SectionName = "SocialDistribution:Facebook";

    /// <summary>The Page access token from Meta for Developers / Graph API Explorer. Sent only in the POST body (never the URL/query string), never logged.</summary>
    public string PageAccessToken { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = "https://graph.facebook.com/";

    /// <summary>Pinned explicitly (rather than defaulting to "latest") so a Meta-side version deprecation is a deliberate config change, not a silent breakage.</summary>
    public string ApiVersion { get; init; } = "v21.0";

    public int TimeoutSeconds { get; init; } = 15;
}
