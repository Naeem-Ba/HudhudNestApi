using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Domain.SocialDistribution.Entities;

/// <summary>
/// Groups every <see cref="SocialPublication"/> produced by ONE evaluation of the distribution
/// rules against ONE property (Phase 4 spec §13) — e.g. "the run triggered when property X was
/// published, which matched 3 rules and created 3 publications". Exists purely for
/// observability/idempotency-scoping: nothing in the state machine of an individual
/// SocialPublication depends on its run.
///
/// A run is created immediately (Status=Pending → Evaluating) and persisted before any
/// SocialPublication is created, specifically so a crash mid-evaluation leaves a durable,
/// inspectable "this run started and didn't finish" record rather than silence — see
/// DistributionEngine.
/// </summary>
public sealed class DistributionRun : AuditableEntity
{
    private DistributionRun() { }

    public Guid PropertyId { get; private set; }

    public DistributionRunTriggerType TriggerType { get; private set; }

    public DistributionRunStatus Status { get; private set; } = DistributionRunStatus.Pending;

    public DateTime? StartedAt { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    /// <summary>How many distinct active rules matched the property (before grouping/winner-selection by account).</summary>
    public int MatchedRuleCount { get; private set; }

    /// <summary>How many SocialPublications this run actually created.</summary>
    public int PublicationsCreatedCount { get; private set; }

    /// <summary>How many matched accounts were skipped (inactive account/channel, or a duplicate active publication already existed).</summary>
    public int SkippedCount { get; private set; }

    /// <summary>Short machine-readable outcome note — e.g. "NoMatchingRules", "AllTargetsIneligible", or a sanitized failure reason. Never raw exception detail.</summary>
    public string? ResultReason { get; private set; }

    public static DistributionRun Create(Guid propertyId, DistributionRunTriggerType triggerType, Guid? triggeredByUserId)
    {
        if (propertyId == Guid.Empty)
            throw new DomainException("لا يمكن بدء عملية توزيع بلا عقار.");

        return new DistributionRun
        {
            PropertyId = propertyId,
            TriggerType = triggerType,
            CreatedByUserId = triggeredByUserId,
        };
    }

    public void MarkEvaluating(DateTime utcNow)
    {
        EnsureStatus(DistributionRunStatus.Pending, "بدء التقييم");
        Status = DistributionRunStatus.Evaluating;
        StartedAt = utcNow;
    }

    /// <summary>
    /// Records the final outcome of the evaluation. Completed/PartiallyCompleted/Failed is
    /// derived from the three counters rather than passed in explicitly, so this method is the
    /// single place that can ever disagree with itself about which of the three applies (spec
    /// §11: "الافتراضي المقترح" — documented in DistributionEngine).
    /// </summary>
    public void Complete(int matchedRuleCount, int publicationsCreatedCount, int skippedCount, DateTime utcNow, string? resultReason)
    {
        EnsureStatus(DistributionRunStatus.Evaluating, "إنهاء عملية التوزيع");

        MatchedRuleCount = matchedRuleCount;
        PublicationsCreatedCount = publicationsCreatedCount;
        SkippedCount = skippedCount;
        ResultReason = string.IsNullOrWhiteSpace(resultReason) ? null : resultReason.Trim();
        CompletedAt = utcNow;

        Status = (publicationsCreatedCount, skippedCount) switch
        {
            (0, 0) => DistributionRunStatus.Completed,       // nothing matched — a valid, non-error outcome (NoMatchingRules)
            ( > 0, 0) => DistributionRunStatus.Completed,      // every matched, eligible account got a publication
            ( > 0, > 0) => DistributionRunStatus.PartiallyCompleted,
            (0, > 0) => DistributionRunStatus.Failed,          // matched something, but every target was ineligible
            _ => DistributionRunStatus.Failed,
        };
    }

    /// <summary>The run could not even start evaluating — e.g. the property was no longer public by the time this ran.</summary>
    public void MarkFailedToStart(string reason, DateTime utcNow)
    {
        if (Status is not (DistributionRunStatus.Pending or DistributionRunStatus.Evaluating))
            throw new InvalidStateTransitionException($"لا يمكن تسجيل فشل بدء التشغيل لعملية توزيع في الحالة '{Status}'.");

        Status = DistributionRunStatus.Failed;
        ResultReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        CompletedAt = utcNow;
    }

    private void EnsureStatus(DistributionRunStatus expected, string action)
    {
        if (Status != expected)
            throw new InvalidStateTransitionException($"لا يمكن '{action}' لعملية توزيع في الحالة '{Status}'.");
    }
}
