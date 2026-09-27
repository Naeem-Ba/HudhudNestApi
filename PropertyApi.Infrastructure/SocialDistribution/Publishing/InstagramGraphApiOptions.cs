namespace PropertyApi.Infrastructure.SocialDistribution.Publishing;

/// <summary>
/// SocialDistribution:Instagram — credentials and transport settings for the real Instagram Graph
/// API (Content Publishing API) integration. The access token is the same kind of token used for
/// <see cref="FacebookGraphApiOptions"/> (a token for the Facebook Page linked to this Instagram
/// professional account), but scoped with <c>instagram_content_publish</c> in addition to the
/// Page permissions — kept as its own option/section (and its own account row —
/// <see cref="Domain.SocialDistribution.Entities.SocialAccount.ExternalAccountId"/> here is the
/// Instagram Business Account id, NOT the Facebook Page id) because the two are genuinely separate
/// accounts in this bounded context, even when one person manages both.
/// </summary>
public sealed class InstagramGraphApiOptions
{
    public const string SectionName = "SocialDistribution:Instagram";

    public string AccessToken { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = "https://graph.facebook.com/";

    public string ApiVersion { get; init; } = "v21.0";

    public int TimeoutSeconds { get; init; } = 15;

    /// <summary>
    /// How many times to retry <c>media_publish</c> when Graph API reports the container isn't
    /// finished processing yet (real, documented async behavior — see
    /// <see cref="InstagramGraphApiPublisher"/>'s remarks) before giving up and returning a
    /// retryable failure for the caller's own outer retry/backoff to pick up instead.
    /// </summary>
    public int ContainerPublishMaxAttempts { get; init; } = 3;

    public int ContainerPublishRetryDelayMilliseconds { get; init; } = 2000;
}
