namespace HudhudNestApi.Application.SocialDistribution.Services;

/// <summary>
/// Thrown by <see cref="ISocialMediaAssetGenerator"/> when generation cannot complete — the
/// caller (<c>PublishSocialPublicationCommandHandler</c>) catches this and fails the publication
/// exactly like a provider failure (spec Phase 7 §23: "لا تنشر Publication" when asset generation
/// fails), classified by <see cref="Retryable"/> into
/// <c>SocialPublicationErrorCode.TemporaryUnavailable</c> (storage/network hiccup — worth another
/// attempt) or <c>SocialPublicationErrorCode.UnsupportedMedia</c> (bad source image, no preset for
/// this platform/asset-type — will fail again unless something changes first).
/// </summary>
public sealed class SocialAssetGenerationException : Exception
{
    public bool Retryable { get; }

    public SocialAssetGenerationException(string message, bool retryable, Exception? innerException = null)
        : base(message, innerException) => Retryable = retryable;
}
