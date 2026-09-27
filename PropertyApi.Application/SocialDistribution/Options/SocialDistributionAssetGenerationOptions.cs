namespace PropertyApi.Application.SocialDistribution.Options;

/// <summary>
/// Section <c>SocialDistribution:AssetGeneration</c>. Gates whether
/// <c>PublishSocialPublicationCommandHandler</c> attaches a freshly-generated branded template
/// asset to an automatic (rule-engine-created) publication before calling the target
/// <c>ISocialPublisher</c>.
///
/// Defaults to <c>false</c> (Phase 1 audit F-4): <c>SocialAssetTemplateRenderer</c> only ever
/// produces <c>image/svg+xml</c>, and none of today's target platforms (Facebook/Instagram/
/// Telegram's real APIs) accept an SVG as a post image — attaching one here would silently make
/// every real publish attempt fail once a real <c>ISocialPublisher</c> exists. With this off, an
/// automatic publication publishes with the same real property photo a manual publication already
/// uses (resolved once, at creation time). Turn this back on once a real raster
/// (JPEG/PNG) rendering pipeline replaces the SVG renderer — the generator, its own tests, and the
/// admin's manual <c>assets/generate</c>/<c>regenerate</c> endpoints are all untouched by this
/// flag; it only gates automatic attachment during a live publish.
/// </summary>
public sealed class SocialDistributionAssetGenerationOptions
{
    public const string SectionName = "SocialDistribution:AssetGeneration";

    public bool AttachGeneratedAssetToAutomaticPublications { get; set; }
}
