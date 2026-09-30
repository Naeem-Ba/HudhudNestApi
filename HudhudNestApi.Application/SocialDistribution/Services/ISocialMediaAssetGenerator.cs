using HudhudNestApi.Application.SocialDistribution.DTOs;

namespace HudhudNestApi.Application.SocialDistribution.Services;

/// <summary>
/// Produces a deterministic, template-based social media image for a property (Phase 7 spec
/// §17). Never calls an AI model, never publishes anywhere, never touches
/// <c>SocialPublication</c>/<c>DistributionRule</c> or the Queue — its only job is "given this
/// property and platform, hand back a ready-to-use image asset". See
/// <c>SocialMediaAssetGenerator</c> for the concrete (template-engine-based) implementation and
/// <c>ISocialContentGenerator</c> (a separate, not-yet-implemented port) for the future AI-content
/// (text, not image) extension point.
/// </summary>
public interface ISocialMediaAssetGenerator
{
    /// <summary>
    /// Throws <see cref="SocialAssetGenerationException"/> on failure — never returns a partial/
    /// invalid result. Reuses a byte-identical previously-generated asset when one exists (spec:
    /// "إعادة استخدام Asset صالح") unless <paramref name="forceRegenerate"/> is true (spec:
    /// "POST /assets/{assetId}/regenerate").
    /// </summary>
    Task<GeneratedSocialAssetResult> GenerateAsync(
        GenerateSocialAssetRequest request,
        bool forceRegenerate = false,
        CancellationToken ct = default);
}
