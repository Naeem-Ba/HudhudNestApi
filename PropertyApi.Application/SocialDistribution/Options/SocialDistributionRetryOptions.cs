namespace PropertyApi.Application.SocialDistribution.Options;

/// <summary>
/// Configuration-bound override for <c>SocialPublicationRetryPolicy</c>'s exponential backoff
/// (Phase 6 spec §12). Defined in Application (not Infrastructure) because it shapes Application
/// behavior (how <c>PublishSocialPublicationCommandHandler</c> schedules a retry), even though the
/// actual <c>IConfiguration</c> binding happens in Infrastructure's DI registration — section
/// <c>SocialDistribution:Retry</c>. Any field left at its default (null / 0) falls back to
/// <c>SocialPublicationRetryPolicy</c>'s own hardcoded defaults, so this section can be partially
/// or entirely omitted from appsettings with no behavior change.
/// </summary>
public sealed class SocialDistributionRetryOptions
{
    public const string SectionName = "SocialDistribution:Retry";

    public TimeSpan? BaseDelay { get; set; }

    public TimeSpan? MaxDelay { get; set; }

    public int? MaxJitterMilliseconds { get; set; }
}
