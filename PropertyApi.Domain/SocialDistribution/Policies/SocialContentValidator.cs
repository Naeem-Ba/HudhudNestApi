using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;

namespace PropertyApi.Domain.SocialDistribution.Policies;

/// <summary>
/// Platform-aware content validation shared by every <see cref="Interfaces.ISocialPublisher"/>
/// adapter — placeholder or real. Extracted from <c>PlatformNotConfiguredPublisherBase</c> (Phase 2
/// spec: a real adapter must not subclass the placeholder base, since it shares nothing with it
/// except this genuinely reusable, platform-agnostic policy check — see that class's own remarks).
/// Every check here is real, documented platform behavior (<see cref="SocialContentPolicy"/> limits
/// + the target <see cref="SocialPublisherCapabilities"/>), never a placeholder concern.
/// </summary>
public static class SocialContentValidator
{
    public static SocialContentValidationResult Validate(
        SocialPublishRequest request, SocialPlatform platform, SocialPublisherCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(request);

        var limits = SocialContentPolicy.GetLimits(platform);
        var errors = new List<string>();

        if (capabilities.RequiresImage && string.IsNullOrWhiteSpace(request.ImageUrl))
            errors.Add($"منصة {platform} تتطلب صورة، والمحتوى لا يحتوي على رابط صورة.");

        if (!string.IsNullOrEmpty(request.ImageUrl) && !SocialContentPolicy.IsValidPublicUrl(request.ImageUrl))
            errors.Add("رابط الصورة غير صالح (يجب أن يكون رابطاً عاماً http/https).");

        if (!SocialContentPolicy.IsValidPublicUrl(request.TargetUrl))
            errors.Add("رابط الوجهة (TargetUrl) غير صالح.");

        if (request.Body.Length > limits.MaxBodyLength)
            errors.Add($"نص المنشور يتجاوز الحد المسموح لمنصة {platform} ({limits.MaxBodyLength} حرفاً).");

        // A photo message's caption is a stricter, separate ceiling from a plain text message's
        // body on some platforms (Telegram: sendPhoto's caption ≤ 1024 vs. sendMessage's 4096) —
        // see SocialPublisherCapabilities.MaxCaptionLengthWithImage's remarks.
        if (!string.IsNullOrEmpty(request.ImageUrl) &&
            capabilities.MaxCaptionLengthWithImage is { } maxCaption &&
            request.Body.Length > maxCaption)
        {
            errors.Add($"نص المنشور مع وجود صورة يتجاوز الحد المسموح لمنصة {platform} ({maxCaption} حرفاً).");
        }

        if (capabilities.MaxTextLength is { } maxText && request.Title.Length > maxText)
            errors.Add($"عنوان المنشور يتجاوز الحد الأقصى المدعوم من هذه المنصة ({maxText} حرفاً).");

        if (!capabilities.SupportsHashtags && request.Hashtags.Count > 0)
            errors.Add($"منصة {platform} لا تدعم الوسوم (Hashtags).");

        return errors.Count == 0 ? SocialContentValidationResult.Valid : SocialContentValidationResult.Invalid(errors.ToArray());
    }
}
