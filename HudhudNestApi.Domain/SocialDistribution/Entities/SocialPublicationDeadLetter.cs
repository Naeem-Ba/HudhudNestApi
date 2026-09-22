using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Domain.SocialDistribution.Entities;

/// <summary>
/// A durable, admin-visible record that one <see cref="SocialPublication"/>'s automatic retry
/// budget was exhausted (or it failed with a non-retryable error) — Phase 6 spec §13: "Dead
/// Letter Handling". Distinct from <see cref="SocialPublication"/>'s own terminal
/// <see cref="SocialPublicationStatus.Failed"/> state: this is a separate ledger an operator
/// reviews and explicitly acts on, never auto-cleared and never auto-requeued (spec: "لا تعيد Job
/// تلقائياً من Dead Letter دون إجراء واضح").
///
/// Never stores secrets — only the sanitized error already recorded on the SocialPublication
/// itself (spec §13: "لا تخزن Secrets داخل Dead Letter Payload").
/// </summary>
public sealed class SocialPublicationDeadLetter : BaseEntity
{
    private SocialPublicationDeadLetter() { }

    public Guid PublicationId { get; private set; }

    public Guid SocialAccountId { get; private set; }

    public SocialPlatform Platform { get; private set; }

    public SocialPublicationErrorCode LastErrorCode { get; private set; }

    public string LastErrorMessage { get; private set; } = string.Empty;

    public int Attempts { get; private set; }

    public DateTime FailedAt { get; private set; }

    public DateTime? ResolvedAt { get; private set; }

    public Guid? ResolvedByUserId { get; private set; }

    public string? ResolutionNote { get; private set; }

    public bool IsResolved => ResolvedAt is not null;

    public static SocialPublicationDeadLetter Create(
        Guid publicationId,
        Guid socialAccountId,
        SocialPlatform platform,
        SocialPublicationErrorCode lastErrorCode,
        string lastErrorMessage,
        int attempts,
        DateTime failedAt)
    {
        if (publicationId == Guid.Empty)
            throw new DomainException("معرّف المنشور مطلوب.");

        return new SocialPublicationDeadLetter
        {
            PublicationId = publicationId,
            SocialAccountId = socialAccountId,
            Platform = platform,
            LastErrorCode = lastErrorCode,
            LastErrorMessage = lastErrorMessage,
            Attempts = attempts,
            FailedAt = failedAt,
        };
    }

    /// <summary>
    /// Marks this dead letter as handled by an operator (spec: "يجب أن يستطيع المسؤول إعادة
    /// الإرسال بعد إصلاح السبب"). Does NOT itself requeue the publication — the caller
    /// (<c>ResolveDeadLetterCommandHandler</c>) separately calls <c>SocialPublication.RetryManually</c>
    /// when the admin explicitly asks to requeue, keeping "acknowledge this failure" and "try
    /// again" as two distinct, auditable actions.
    /// </summary>
    public void Resolve(Guid? resolvedByUserId, string? note, DateTime utcNow)
    {
        if (IsResolved)
            throw new InvalidStateTransitionException("تم التعامل مع هذا الفشل مسبقاً.");

        ResolvedAt = utcNow;
        ResolvedByUserId = resolvedByUserId;
        ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
