using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.SocialDistribution.Attribution;
using PropertyApi.Domain.SocialDistribution.Enums;

namespace PropertyApi.Domain.SocialDistribution.Entities;

/// <summary>
/// One attempt to distribute a property listing to one social account (spec §6.3) — the
/// aggregate root of this bounded context. Owns the <see cref="SocialPublicationStatus"/> state
/// machine and every retry/scheduling field; <see cref="Entities.SocialPostContent"/> is a
/// dependent 1:1 child, never merged into this entity (keeps "what text" separate from "what
/// happened").
///
/// DDD: private setters + factory method + domain state-machine methods, mirroring
/// ServiceRequest/VisitRequest elsewhere in this codebase. See <see cref="SocialPublicationStatus"/>
/// for the full transition map.
/// </summary>
public sealed class SocialPublication : AuditableEntity
{
    /// <summary>A publish attempt is never retried indefinitely — spec §12 "تحديد MaxRetryCount".</summary>
    public const int DefaultMaxRetryCount = 5;

    private SocialPublication() { }

    public Guid PropertyId { get; private set; }

    public Guid SocialAccountId { get; private set; }

    public SocialPublicationStatus Status { get; private set; } = SocialPublicationStatus.Draft;

    public DateTime? ScheduledAt { get; private set; }

    public DateTime? StartedAt { get; private set; }

    public DateTime? PublishedAt { get; private set; }

    public DateTime? FailedAt { get; private set; }

    public DateTime? CancelledAt { get; private set; }

    public string? ExternalPostId { get; private set; }

    public string? ExternalPostUrl { get; private set; }

    public SocialPublicationErrorCode? ErrorCode { get; private set; }

    /// <summary>Sanitized, capped failure detail — never the raw exception/HTTP body (spec §12/§22).</summary>
    public string? ErrorMessage { get; private set; }

    public int RetryCount { get; private set; }

    public int MaxRetryCount { get; private set; } = DefaultMaxRetryCount;

    public DateTime? LastRetryAt { get; private set; }

    public DateTime? NextRetryAt { get; private set; }

    /// <summary>
    /// Set only while <see cref="Status"/> is <see cref="SocialPublicationStatus.Publishing"/> —
    /// the deadline by which the worker holding this row must have recorded an outcome
    /// (<see cref="MarkPublished"/>/<see cref="MarkFailed"/>). A row still Publishing past this
    /// deadline means the process that started it never got to record what happened (crash,
    /// restart, killed container) — <see cref="ReleaseExpiredLease"/> is how that gets resolved
    /// without ever risking a duplicate post. Cleared whenever the row leaves Publishing.
    /// </summary>
    public DateTime? LeaseUntil { get; private set; }

    // ── UTM / Attribution (spec §19) — set once at creation, immutable afterwards ──────────
    public string UtmSource { get; private set; } = string.Empty;
    public string UtmMedium { get; private set; } = string.Empty;
    public string UtmCampaign { get; private set; } = string.Empty;
    public string UtmContent { get; private set; } = string.Empty;

    public SocialPostContent? Content { get; private set; }

    /// <summary>
    /// The <c>DistributionRule</c> (Phase 4) whose evaluation created this publication — null for
    /// a publication created directly through the manual API (spec §14: "أي قاعدة أنشأت
    /// المنشور؟"). FK-only, no navigation property — SocialDistribution's own DistributionRule
    /// lives in the same bounded context, but this aggregate still must not need to know its
    /// shape, only its id.
    /// </summary>
    public Guid? DistributionRuleId { get; private set; }

    /// <summary>The <c>DistributionRun</c> (Phase 4) this publication was created as part of — null for a manually-created publication. See <see cref="DistributionRuleId"/>.</summary>
    public Guid? DistributionRunId { get; private set; }

    private const int MaxErrorMessageLength = 500;

    /// <summary>
    /// <paramref name="platform"/> is the target SocialAccount's platform, supplied by the
    /// caller (which already loaded the account) — not stored back on this entity directly, it
    /// only seeds the UTM defaults below (spec §19), computed here rather than in Application so
    /// there is exactly one place that can produce them and every SocialPublication is
    /// guaranteed to carry them.
    ///
    /// <paramref name="distributionRuleId"/>/<paramref name="distributionRunId"/> are optional
    /// (Phase 4): left null for the manual "create one publication by hand" API flow that
    /// predates the rule engine; stamped by <c>DistributionEngine</c> when a publication is the
    /// automatic product of rule evaluation.
    /// </summary>
    /// <param name="isPromotionalRepost">
    /// Phase 13 spec §"مثال UTM للترويج": true for a deliberate repost/promotion (a distinct
    /// <c>utm_campaign=property_promotion</c>/<c>utm_content=promotion_{propertyId}</c> pair, so
    /// reporting can separate this from routine auto-distribution traffic) — false (default)
    /// keeps the exact pre-existing UTM behavior for every other caller.
    /// </param>
    public static SocialPublication Create(
        Guid propertyId,
        Guid socialAccountId,
        Guid createdByUserId,
        SocialPlatform platform,
        int maxRetryCount = DefaultMaxRetryCount,
        Guid? distributionRuleId = null,
        Guid? distributionRunId = null,
        bool isPromotionalRepost = false)
    {
        if (propertyId == Guid.Empty)
            throw new DomainException("لا يمكن إنشاء منشور توزيع بلا عقار.");

        if (socialAccountId == Guid.Empty)
            throw new DomainException("لا يمكن إنشاء منشور توزيع بلا حساب اجتماعي.");

        if (maxRetryCount < 0)
            throw new DomainException("الحد الأقصى لعدد إعادة المحاولات لا يمكن أن يكون سالباً.");

        var publication = new SocialPublication
        {
            PropertyId = propertyId,
            SocialAccountId = socialAccountId,
            CreatedByUserId = createdByUserId,
            MaxRetryCount = maxRetryCount,
            DistributionRuleId = distributionRuleId,
            DistributionRunId = distributionRunId,
            UtmMedium = SocialDistributionUtmDefaults.UtmMedium,
            UtmCampaign = isPromotionalRepost
                ? SocialDistributionUtmDefaults.UtmCampaignPromotion
                : SocialDistributionUtmDefaults.UtmCampaign,
            UtmSource = SocialDistributionUtmDefaults.UtmSourceFor(platform),
        };

        // Depends on Id, which BaseEntity's field initializer already assigned above.
        publication.UtmContent = isPromotionalRepost
            ? SocialDistributionUtmDefaults.UtmContentForPromotion(propertyId)
            : SocialDistributionUtmDefaults.UtmContentForPublication(publication.Id);

        return publication;
    }

    /// <summary>Attaches the prepared content. Called once, right after <see cref="Create"/>, by the command handler.</summary>
    public void AttachContent(SocialPostContent content)
    {
        if (content is null)
            throw new DomainException("محتوى المنشور مطلوب.");

        if (content.PublicationId != Id)
            throw new DomainException("لا يمكن ربط محتوى تابع لمنشور آخر.");

        if (Content is not null)
            throw new InvalidStateTransitionException("هذا المنشور يملك محتوى مسبقاً.");

        Content = content;
    }

    /// <summary>Draft → Queued. <paramref name="scheduledAt"/> null means "publish as soon as the worker/endpoint picks it up".</summary>
    public void Queue(DateTime? scheduledAt, DateTime utcNow)
    {
        EnsureStatus(SocialPublicationStatus.Draft, "وضع في قائمة الانتظار (Queue)");

        if (Content is null)
            throw new InvalidStateTransitionException("لا يمكن جدولة/تفعيل منشور بلا محتوى.");

        // Phase 8 spec §3: content pending human review (or rejected) must never reach the
        // queue — a future AI-assisted generator that flags its own output unsafe relies on this
        // gate, not on every caller remembering to check ReviewStatus itself.
        if (Content.ReviewStatus != ContentReviewStatus.Approved)
        {
            throw new InvalidStateTransitionException(
                $"لا يمكن جدولة/تفعيل منشور محتواه بانتظار المراجعة أو مرفوض (الحالة: {Content.ReviewStatus}).");
        }

        if (scheduledAt is not null && scheduledAt < utcNow)
            throw new DomainException("لا يمكن جدولة النشر في وقت ماضٍ.");

        Status = SocialPublicationStatus.Queued;
        ScheduledAt = scheduledAt;
    }

    /// <summary>
    /// Changes a still-Queued publication's scheduled time (Phase 12 spec §12: "إمكانية تعديل
    /// الجدولة قبل التنفيذ") — deliberately a separate method from <see cref="Queue"/>, which only
    /// works from Draft: once queued, this is the only way to move the time, and only while the
    /// worker has not yet picked it up (Queued only — never Publishing/Published/Retrying, where
    /// a reschedule could race a publish attempt already in flight or already completed).
    /// </summary>
    public void Reschedule(DateTime? newScheduledAt, DateTime utcNow)
    {
        EnsureStatus(SocialPublicationStatus.Queued, "إعادة الجدولة");

        if (newScheduledAt is not null && newScheduledAt < utcNow)
            throw new DomainException("لا يمكن جدولة النشر في وقت ماضٍ.");

        ScheduledAt = newScheduledAt;
    }

    /// <summary>Safety margin for <see cref="ReleaseExpiredLease"/> — comfortably longer than any real publish call, including retries/timeouts inside a single ISocialPublisher call.</summary>
    public static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Queued|Retrying → Publishing. This is the ONLY entry point the worker/manual-publish use
    /// case may call before actually invoking an ISocialPublisher — it is what makes a second,
    /// concurrent execution of the same publication a state-transition error instead of a
    /// duplicate external post (spec §9.11/§9.12 idempotency).
    ///
    /// The caller MUST persist this transition (a real SaveChangesAsync) before making the actual
    /// external call — seeing this row committed as Publishing+LeaseUntil is what lets a crash
    /// mid-call be recovered safely by <see cref="ReleaseExpiredLease"/> instead of the next sweep
    /// silently re-selecting a row that is still, at rest, Queued.
    /// </summary>
    public void StartPublishing(DateTime utcNow, TimeSpan? leaseDuration = null)
    {
        if (Status is not (SocialPublicationStatus.Queued or SocialPublicationStatus.Retrying))
        {
            throw new InvalidStateTransitionException(
                $"لا يمكن بدء النشر لمنشور في الحالة '{Status}'.");
        }

        Status = SocialPublicationStatus.Publishing;
        StartedAt = utcNow;
        LeaseUntil = utcNow.Add(leaseDuration ?? DefaultLeaseDuration);
    }

    public void MarkPublished(string externalPostId, string? externalPostUrl, DateTime utcNow)
    {
        EnsureStatus(SocialPublicationStatus.Publishing, "تأكيد النشر");

        if (string.IsNullOrWhiteSpace(externalPostId))
            throw new DomainException("معرّف المنشور الخارجي (ExternalPostId) مطلوب لتأكيد النجاح.");

        Status = SocialPublicationStatus.Published;
        ExternalPostId = externalPostId.Trim();
        ExternalPostUrl = string.IsNullOrWhiteSpace(externalPostUrl) ? null : externalPostUrl.Trim();
        PublishedAt = utcNow;
        ErrorCode = null;
        ErrorMessage = null;
        LeaseUntil = null;
    }

    /// <summary>
    /// Publishing → Failed (or → Retrying if the error is retryable and the retry budget is not
    /// exhausted — spec §12: never retry a non-retryable error, never exceed MaxRetryCount).
    /// </summary>
    public void MarkFailed(SocialPublicationErrorCode errorCode, string errorMessage, DateTime utcNow, TimeSpan retryDelay)
    {
        EnsureStatus(SocialPublicationStatus.Publishing, "تسجيل فشل النشر");

        ErrorCode = errorCode;
        ErrorMessage = Truncate(errorMessage, MaxErrorMessageLength);
        FailedAt = utcNow;
        LeaseUntil = null;

        if (errorCode.IsRetryable() && RetryCount < MaxRetryCount)
        {
            Status = SocialPublicationStatus.Retrying;
            RetryCount += 1;
            LastRetryAt = utcNow;
            NextRetryAt = utcNow.Add(retryDelay);
        }
        else
        {
            Status = SocialPublicationStatus.Failed;
            NextRetryAt = null;
        }
    }

    /// <summary>
    /// Recovers a publication whose worker never came back to record an outcome (crash/restart
    /// mid-publish) — see <see cref="LeaseUntil"/>. Deliberately always terminal
    /// (<see cref="SocialPublicationErrorCode.AmbiguousOutcome"/>, never retryable): we cannot
    /// tell whether the interrupted attempt actually reached the platform, and auto-retrying an
    /// unknown outcome risks a genuine duplicate post — a human resolves it via the existing
    /// dead-letter workflow instead. A no-op if the lease has not actually expired, or the row
    /// already left Publishing by some other path (nothing to release).
    /// </summary>
    public void ReleaseExpiredLease(DateTime utcNow)
    {
        if (Status != SocialPublicationStatus.Publishing)
            return;

        if (LeaseUntil is null || LeaseUntil > utcNow)
            return;

        MarkFailed(
            SocialPublicationErrorCode.AmbiguousOutcome,
            "تعطّل العامل الخلفي أثناء محاولة النشر ولم يُعرف ما إذا تم النشر فعلياً على المنصة — يتطلب تحققاً يدوياً.",
            utcNow,
            TimeSpan.Zero);
    }

    /// <summary>
    /// Manual retry from Failed (spec §12: "السماح بإعادة المحاولة اليدوية من لوحة الإدارة").
    /// Resets straight back to Queued for the worker/endpoint to pick up again — does NOT bypass
    /// MaxRetryCount, so an operator cannot use manual retry to loop forever on a permanently
    /// broken integration either.
    /// </summary>
    public void RetryManually(DateTime utcNow)
    {
        EnsureStatus(SocialPublicationStatus.Failed, "إعادة المحاولة يدوياً");

        if (RetryCount >= MaxRetryCount)
        {
            throw new InvalidStateTransitionException(
                "تم تجاوز الحد الأقصى لعدد إعادة المحاولات (MaxRetryCount) لهذا المنشور.");
        }

        Status = SocialPublicationStatus.Queued;
        RetryCount += 1;
        LastRetryAt = utcNow;
        NextRetryAt = null;
        ScheduledAt = null;
    }

    /// <summary>The scheduled-retry worker's counterpart to <see cref="RetryManually"/>: Retrying → Publishing goes through <see cref="StartPublishing"/> directly, so this simply exists as the query predicate the worker uses — see ISocialPublicationRepository.</summary>
    public bool IsDueForRetry(DateTime utcNow) =>
        Status == SocialPublicationStatus.Retrying && (NextRetryAt is null || NextRetryAt <= utcNow);

    public bool IsDueToPublish(DateTime utcNow) =>
        Status == SocialPublicationStatus.Queued && (ScheduledAt is null || ScheduledAt <= utcNow);

    /// <summary>
    /// Draft|Queued|Failed|Retrying → Cancelled. Published is terminal in the other direction —
    /// once a post is actually live, cancelling the SocialPublication record cannot un-publish it
    /// (spec §9.14: "لا يمكن تعديل Publication Published مباشرة"), so Cancel from Published is
    /// deliberately rejected rather than silently accepted.
    /// </summary>
    public void Cancel(DateTime utcNow)
    {
        if (Status is SocialPublicationStatus.Published or SocialPublicationStatus.Publishing or SocialPublicationStatus.Cancelled)
        {
            throw new InvalidStateTransitionException(
                $"لا يمكن إلغاء منشور توزيع في الحالة '{Status}'.");
        }

        Status = SocialPublicationStatus.Cancelled;
        CancelledAt = utcNow;
    }

    private void EnsureStatus(SocialPublicationStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new InvalidStateTransitionException(
                $"لا يمكن '{action}' لمنشور توزيع في الحالة '{Status}'.");
        }
    }

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value)
            ? string.Empty
            : (value.Length <= maxLength ? value : value[..maxLength]);
}
