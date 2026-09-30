namespace HudhudNestApi.Application.SocialDistribution.Interfaces;

/// <summary>
/// Backend equivalent of the frontend's PropertyShareUrlService + UtmAttributionService
/// (Phase 1/2, HudhudNest repo) — needed here because a distribution post is prepared and
/// published server-side, with no browser/Angular runtime involved to build the link. Reads the
/// same <c>Frontend:BaseUrl</c> configuration the rest of this backend already uses for
/// user-facing links (PasswordResetUrlBuilder, EmailConfirmationUrlBuilder).
/// </summary>
public interface ISocialDistributionTargetUrlBuilder
{
    /// <summary>
    /// Builds the absolute, UTM-attributed property URL a distribution post should link to.
    /// Never mutates or reads the canonical/OG URL used elsewhere — this is a wholly separate,
    /// additive link (spec §19: never add UTM to canonical/og:url).
    /// </summary>
    string BuildAttributedTargetUrl(
        Guid propertyId,
        string utmSource,
        string utmMedium,
        string utmCampaign,
        string utmContent);
}
