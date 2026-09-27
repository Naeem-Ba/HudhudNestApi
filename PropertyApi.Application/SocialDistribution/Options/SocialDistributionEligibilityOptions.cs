namespace PropertyApi.Application.SocialDistribution.Options;

/// <summary>
/// The automatic quality gate a listing must clear before <see cref="Services.DistributionEngine"/>
/// even attempts to distribute it — section <c>SocialDistribution:Eligibility</c>. This runs once
/// per property, before any rule is evaluated (an ineligible listing never reaches a single
/// account), and is distinct from <see cref="SocialDistributionContentReviewOptions"/>, which
/// gates the CONTENT of an otherwise-eligible listing.
///
/// Deliberately conservative defaults: <c>PublishPropertyCommandHandler</c> already requires at
/// least one non-deleted image before a listing can go live at all, so <see cref="MinImageCount"/>
/// = 1 changes nothing for a normal listing — it only guards against an image being deleted after
/// publish. <see cref="MinDescriptionLength"/> is a low floor (not a content-quality judgment);
/// raise either value from configuration once real posting is live and there is a track record to
/// tune against.
/// </summary>
public sealed class SocialDistributionEligibilityOptions
{
    public const string SectionName = "SocialDistribution:Eligibility";

    public int MinImageCount { get; set; } = 1;

    public int MinDescriptionLength { get; set; } = 20;
}
