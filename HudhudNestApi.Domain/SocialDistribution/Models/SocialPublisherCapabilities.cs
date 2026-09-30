namespace HudhudNestApi.Domain.SocialDistribution.Models;

/// <summary>
/// What a given <see cref="Interfaces.ISocialPublisher"/> platform actually supports (Phase 5
/// spec §7). Domain/Application check this BEFORE creating a job or invoking the publisher — a
/// platform that cannot do Stories must never be asked to publish one, and content that exceeds
/// <see cref="MaxTextLength"/>/<see cref="MaxImages"/> must be rejected up front rather than sent
/// to a real API only to fail there.
///
/// Each concrete <see cref="Interfaces.ISocialPublisher"/> owns its own capabilities — the engine
/// never hardcodes "Instagram requires an image" anywhere; it asks the publisher.
/// </summary>
/// <param name="SupportsComment">Can a comment be added beneath an already-published post (Phase 11 spec §7)? Defaults true — most platforms support this even when they don't support Update/Delete.</param>
public sealed record SocialPublisherCapabilities(
    bool SupportsText,
    bool SupportsImages,
    bool SupportsVideo,
    bool SupportsStories,
    bool SupportsHashtags,
    bool SupportsScheduling,
    bool SupportsUpdate,
    bool SupportsDelete,
    int? MaxTextLength = null,
    int? MaxImages = null,
    bool RequiresImage = false,
    bool SupportsComment = true,
    /// <summary>
    /// A platform-specific ceiling on the BODY (not the title) that only applies when an image is
    /// also attached — real, well-documented Telegram Bot API behavior: <c>sendMessage</c> allows
    /// up to 4096 characters, but <c>sendPhoto</c>'s caption tops out at 1024. Null (the default)
    /// means no such photo-specific ceiling exists for this platform, so only
    /// <see cref="SocialContentPolicy.PlatformLimits.MaxBodyLength"/> applies.
    /// </summary>
    int? MaxCaptionLengthWithImage = null);
