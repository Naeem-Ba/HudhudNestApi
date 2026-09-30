using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Policies;

namespace HudhudNestApi.Domain.SocialDistribution.Entities;

/// <summary>
/// The actual text/media prepared for one <see cref="SocialPublication"/> — deliberately its own
/// aggregate, never a field on <c>Property</c> (spec §7: "لا تجعل النص النهائي للمنشور جزءًا من
/// Property"). One property can have several publications (one per platform/account/language),
/// each with its own SocialPostContent, so the same listing can carry a Facebook caption in
/// Arabic and a LinkedIn caption in English without either living anywhere near the Property
/// entity itself.
///
/// Design decision — 1:1 with SocialPublication, not 1:*: the spec allows either shape. A
/// publication that needs different wording for a different audience is, in this design, a
/// second SocialPublication (new SocialAccount and/or Language), not a second content row under
/// the same one — this keeps "which exact text did platform X actually receive" a single,
/// unambiguous answer per publication, at the cost of not supporting in-place content
/// A/B-variants under one publication (documented limitation — see final report).
/// </summary>
public sealed class SocialPostContent : BaseEntity
{
    private SocialPostContent() { }

    public Guid PublicationId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    /// <summary>Must be a public, absolute http(s) URL — never a private/authenticated asset (spec §18).</summary>
    public string ImageUrl { get; private set; } = string.Empty;

    /// <summary>The attributed public property URL this post links to. Never an internal SPA route.</summary>
    public string TargetUrl { get; private set; } = string.Empty;

    /// <summary>Comma-joined, normalized (no leading '#', deduplicated, platform-capped) hashtags. Empty string, never null, when there are none.</summary>
    public string Hashtags { get; private set; } = string.Empty;

    /// <summary>ISO-639-1 code — "ar" or "en" today (spec §18).</summary>
    public string Language { get; private set; } = "ar";

    public SocialPlatform Platform { get; private set; }

    /// <summary>Bumped by <see cref="Revise"/>. Starts at 1.</summary>
    public int ContentVersion { get; private set; } = 1;

    public IReadOnlyList<string> HashtagList => Hashtags.Length == 0
        ? Array.Empty<string>()
        : Hashtags.Split(',');

    /// <summary>
    /// The <c>SocialMediaAsset</c> (Phase 7) currently backing <see cref="ImageUrl"/>, if any —
    /// null for content still using a plain property photo (manual publications, or before the
    /// worker has generated a branded asset for an automatic one). FK-only, no navigation
    /// property, same pattern as every other cross-aggregate reference in this bounded context.
    /// </summary>
    public Guid? SocialMediaAssetId { get; private set; }

    /// <summary>Human-review workflow (Phase 8 spec §3). Defaults to <see cref="ContentReviewStatus.Approved"/> — see enum docs.</summary>
    public ContentReviewStatus ReviewStatus { get; private set; } = ContentReviewStatus.Approved;

    public Guid? ReviewedByUserId { get; private set; }

    public DateTime? ReviewedAt { get; private set; }

    /// <summary>Reviewer's note — required on <see cref="Reject"/>, e.g. "يتعارض مع بيانات العقار".</summary>
    public string? ReviewNote { get; private set; }

    public static SocialPostContent Create(
        Guid publicationId,
        SocialPlatform platform,
        string title,
        string body,
        string imageUrl,
        string targetUrl,
        IEnumerable<string>? hashtags,
        string language)
    {
        if (publicationId == Guid.Empty)
            throw new DomainException("معرّف المنشور مطلوب لإنشاء محتوى.");

        if (!Enum.IsDefined(platform))
            throw new DomainException("منصة اجتماعية غير معروفة.");

        var sanitizedTitle = SocialContentPolicy.SanitizePlainText(title);
        if (sanitizedTitle.Length == 0)
            throw new DomainException("عنوان المنشور مطلوب.");
        if (!SocialContentPolicy.FitsTitleLimit(sanitizedTitle, platform))
            throw new DomainException($"عنوان المنشور يتجاوز الحد المسموح لهذه المنصة ({SocialContentPolicy.GetLimits(platform).MaxTitleLength} حرفاً).");

        var sanitizedBody = SocialContentPolicy.SanitizePlainText(body);
        if (sanitizedBody.Length == 0)
            throw new DomainException("نص المنشور مطلوب.");
        if (!SocialContentPolicy.FitsBodyLimit(sanitizedBody, platform))
            throw new DomainException($"نص المنشور يتجاوز الحد المسموح لهذه المنصة ({SocialContentPolicy.GetLimits(platform).MaxBodyLength} حرفاً).");

        if (!SocialContentPolicy.IsValidPublicUrl(imageUrl))
            throw new DomainException("رابط الصورة يجب أن يكون رابطاً عاماً صالحاً (http/https).");

        if (!SocialContentPolicy.IsValidPublicUrl(targetUrl))
            throw new DomainException("رابط الوجهة (TargetUrl) يجب أن يكون رابطاً عاماً صالحاً (http/https), وليس مساراً داخلياً.");

        if (!SocialContentPolicy.IsSupportedLanguage(language))
            throw new DomainException("لغة غير مدعومة. القيم المدعومة حالياً: ar, en.");

        var normalizedHashtags = SocialContentPolicy.NormalizeHashtags(hashtags, platform);

        return new SocialPostContent
        {
            PublicationId = publicationId,
            Platform = platform,
            Title = sanitizedTitle,
            Body = sanitizedBody,
            ImageUrl = imageUrl.Trim(),
            TargetUrl = targetUrl.Trim(),
            Hashtags = string.Join(',', normalizedHashtags),
            Language = SocialContentPolicy.NormalizeLanguage(language),
        };
    }

    /// <summary>
    /// Attaches a freshly-generated (or reused) <c>SocialMediaAsset</c> as this content's image —
    /// called only by <c>PublishSocialPublicationCommandHandler</c>, right before publishing, for
    /// automatically-distributed content (spec Phase 7 §23: "يتم ربط Asset بـSocialPostContent").
    /// Distinct from <see cref="Revise"/>: this does not bump <see cref="ContentVersion"/> — it is
    /// the system attaching what the content SHOULD display, not an editorial revision, and must
    /// not be confused with an admin manually re-writing the caption.
    /// </summary>
    public void AttachGeneratedAsset(Guid assetId, string generatedImageUrl)
    {
        if (assetId == Guid.Empty)
            throw new DomainException("معرّف الصورة المولّدة مطلوب.");

        if (!SocialContentPolicy.IsValidPublicUrl(generatedImageUrl))
            throw new DomainException("رابط الصورة المولّدة يجب أن يكون رابطاً عاماً صالحاً (http/https).");

        SocialMediaAssetId = assetId;
        ImageUrl = generatedImageUrl.Trim();
    }

    /// <summary>
    /// Puts this content into <see cref="ContentReviewStatus.PendingReview"/> (Phase 8 spec §3) —
    /// called by a (future) content generator that flags its own output as unsafe to auto-queue
    /// (e.g. an AI generator uncertain about a claim). <see cref="Entities.SocialPublication.Queue"/>
    /// refuses to queue content in this state.
    /// </summary>
    public void RequireReview(string reason)
    {
        ReviewStatus = ContentReviewStatus.PendingReview;
        ReviewNote = string.IsNullOrWhiteSpace(reason) ? null : SocialContentPolicy.SanitizePlainText(reason);
        ReviewedByUserId = null;
        ReviewedAt = null;
    }

    /// <summary>PendingReview|Rejected → Approved. An admin/editor confirms the content is safe to queue.</summary>
    public void Approve(Guid reviewedByUserId, DateTime utcNow, string? note = null)
    {
        ReviewStatus = ContentReviewStatus.Approved;
        ReviewedByUserId = reviewedByUserId;
        ReviewedAt = utcNow;
        ReviewNote = string.IsNullOrWhiteSpace(note) ? null : SocialContentPolicy.SanitizePlainText(note);
    }

    /// <summary>PendingReview → Rejected. Rejected content can never be queued until a subsequent <see cref="Revise"/> + <see cref="Approve"/>.</summary>
    public void Reject(Guid reviewedByUserId, string note, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(note))
            throw new DomainException("سبب رفض المحتوى مطلوب.");

        ReviewStatus = ContentReviewStatus.Rejected;
        ReviewedByUserId = reviewedByUserId;
        ReviewedAt = utcNow;
        ReviewNote = SocialContentPolicy.SanitizePlainText(note);
    }

    /// <summary>
    /// Content can only be revised while its publication is still a Draft — the caller (the
    /// command handler, which already holds the parent SocialPublication) is responsible for
    /// that check; this entity has no reference back to its publication's status by design
    /// (a content row must never need to know about — or enforce — its aggregate root's state
    /// machine).
    /// </summary>
    public void Revise(string title, string body, string imageUrl, IEnumerable<string>? hashtags)
    {
        var sanitizedTitle = SocialContentPolicy.SanitizePlainText(title);
        if (sanitizedTitle.Length == 0 || !SocialContentPolicy.FitsTitleLimit(sanitizedTitle, Platform))
            throw new DomainException("عنوان المنشور غير صالح لهذه المنصة.");

        var sanitizedBody = SocialContentPolicy.SanitizePlainText(body);
        if (sanitizedBody.Length == 0 || !SocialContentPolicy.FitsBodyLimit(sanitizedBody, Platform))
            throw new DomainException("نص المنشور غير صالح لهذه المنصة.");

        if (!SocialContentPolicy.IsValidPublicUrl(imageUrl))
            throw new DomainException("رابط الصورة يجب أن يكون رابطاً عاماً صالحاً (http/https).");

        Title = sanitizedTitle;
        Body = sanitizedBody;
        ImageUrl = imageUrl.Trim();
        Hashtags = string.Join(',', SocialContentPolicy.NormalizeHashtags(hashtags, Platform));
        ContentVersion += 1;

        // A previously-rejected caption that gets edited must be re-reviewed, never silently
        // re-admitted through the back door of an edit (spec §3: rejected content stays blocked
        // from Queue until a human looks at it again).
        if (ReviewStatus == ContentReviewStatus.Rejected)
            RequireReview("تمت مراجعة النص بعد رفضه السابق ويلزم اعتماده من جديد.");
    }
}
