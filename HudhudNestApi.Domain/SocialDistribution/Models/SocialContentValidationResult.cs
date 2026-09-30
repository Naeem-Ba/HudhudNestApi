namespace HudhudNestApi.Domain.SocialDistribution.Models;

/// <summary>
/// Result of asking an <see cref="Interfaces.ISocialPublisher"/> whether it can actually accept a
/// given <see cref="SocialPublishRequest"/> (Phase 5 spec §4/§7) — checked BEFORE calling
/// <see cref="Interfaces.ISocialPublisher.PublishAsync"/>, so an over-length caption or a
/// missing-but-required image is rejected as a local, non-retryable
/// <c>SocialPublicationErrorCode.InvalidContent</c> instead of burning a real API call.
/// </summary>
public sealed record SocialContentValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static readonly SocialContentValidationResult Valid = new(true, Array.Empty<string>());

    public static SocialContentValidationResult Invalid(params string[] errors) => new(false, errors);
}
