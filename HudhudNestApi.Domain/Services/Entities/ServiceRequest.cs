using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Domain.Services.Entities;

/// <summary>
/// The central marketplace entity: one user's request for a service, against one property,
/// fulfilled by one ServiceProvider through one ServiceOffering.
///
/// DDD: private setters + factory method + domain state-machine methods, mirroring
/// VisitRequest. See <see cref="Enums.ServiceRequestStatus"/> for the full transition map.
///
/// Authorization is deliberately NOT enforced inside this entity for actions available to
/// more than one actor type (Accept/Reject/Schedule/Start/Complete/Cancel): unlike
/// VisitRequest.Cancel, which can compare RequesterId to a single actor field directly, a
/// provider-side action here needs to know ServiceProvider.UserId, which this entity does not
/// hold. That check happens once in the command handler (which already loads the
/// ServiceProvider to validate the offering) before calling these methods — the entity still
/// owns 100% of the *state* rules, just not the *actor* rules for the multi-actor methods.
/// RequesterId-only checks (e.g. "is this actor the requester") are cheap enough that handlers
/// do them the same way as everywhere else in this codebase: compare the id, no service needed.
/// </summary>
public sealed class ServiceRequest : AuditableEntity
{
    private ServiceRequest() { }

    /// <summary>
    /// Human-facing unique identifier, e.g. "SR-2026-000001". Server-generated exclusively —
    /// see IServiceRequestNumberGenerator. Never client-suppliable, never regenerated.
    /// </summary>
    public string RequestNumber { get; private set; } = string.Empty;

    public Guid PropertyId { get; private set; }

    public Guid RequesterId { get; private set; }

    public Guid ServiceProviderId { get; private set; }

    public Guid ServiceOfferingId { get; private set; }

    /// <summary>Denormalized from the offering at creation time — the category never changes.</summary>
    public ServiceCategory Category { get; private set; }

    public ServiceRequestStatus Status { get; private set; } = ServiceRequestStatus.Submitted;

    public string? RequesterNote { get; private set; }

    public string? ProviderNote { get; private set; }

    public DateTime? ScheduledAt { get; private set; }

    public decimal? QuotedPrice { get; private set; }

    public int? QuotedPriceCurrencyId { get; private set; }

    public decimal? FinalPrice { get; private set; }

    public int? FinalPriceCurrencyId { get; private set; }

    public string? RejectionReason { get; private set; }

    public string? CancellationReason { get; private set; }

    public DateTime? CompletedAt { get; private set; }

    // ── Outcome, as declared by the provider (NotCompleted) ──────────
    /// <summary>Structured reason for Rejected or NotCompleted. Null for older rows and other states.</summary>
    public ServiceOutcomeReason? OutcomeReason { get; private set; }

    public string? OutcomeNote { get; private set; }

    public DateTime? NotCompletedAt { get; private set; }

    // ── The customer's side ──────────────────────────────────────────
    /// <summary>Null until the customer answers "is the provider's outcome right?".</summary>
    public ServiceRequesterAnswer? RequesterAnswer { get; private set; }

    /// <summary>The customer's own reason: set when the service did not happen, when they
    /// dispute the outcome, or when they cancel. Null otherwise.</summary>
    public ServiceRequesterReason? RequesterReason { get; private set; }

    public string? RequesterFeedbackNote { get; private set; }

    public DateTime? RequesterFeedbackAt { get; private set; }

    // ── Navigation (EF) ───────────────────────────────────────────
    public HudhudNestApi.Domain.Listings.Entities.Property? Property { get; private set; }

    public HudhudNestApi.Domain.Users.Entities.UserAccount? Requester { get; private set; }

    public ServiceProvider? ServiceProvider { get; private set; }

    public ServiceOffering? ServiceOffering { get; private set; }

    public static ServiceRequest Create(
        string requestNumber,
        Guid propertyId,
        Guid requesterId,
        Guid serviceProviderId,
        Guid serviceOfferingId,
        ServiceCategory category,
        string? requesterNote,
        DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(requestNumber))
            throw new DomainException("رقم الطلب مطلوب.");

        if (propertyId == Guid.Empty)
            throw new DomainException("لا يمكن إنشاء طلب خدمة بلا عقار.");

        if (requesterId == Guid.Empty)
            throw new DomainException("لا يمكن إنشاء طلب خدمة بلا مستخدم طالب.");

        if (serviceProviderId == Guid.Empty || serviceOfferingId == Guid.Empty)
            throw new DomainException("لا يمكن إنشاء طلب خدمة بلا مزوّد وخدمة محددَين.");

        return new ServiceRequest
        {
            RequestNumber = requestNumber.Trim(),
            PropertyId = propertyId,
            RequesterId = requesterId,
            ServiceProviderId = serviceProviderId,
            ServiceOfferingId = serviceOfferingId,
            Category = category,
            RequesterNote = string.IsNullOrWhiteSpace(requesterNote) ? null : requesterNote.Trim(),
            // Skips directly to UnderReview — see the enum's remarks: the MVP has no real
            // triage/routing queue that would hold a request at "Submitted" for any real
            // duration, so there is nothing for a request to wait on between the two states.
            Status = ServiceRequestStatus.UnderReview,
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }

    public void Accept(decimal? quotedPrice, int? quotedPriceCurrencyId, string? providerNote, DateTime utcNow)
    {
        EnsureStatus(ServiceRequestStatus.UnderReview, "قبول");

        if (quotedPrice is < 0)
            throw new DomainException("السعر المقترح لا يمكن أن يكون سالباً.");

        if (quotedPrice is not null && quotedPriceCurrencyId is null)
            throw new DomainException("عملة السعر المقترح مطلوبة عند تحديد سعر.");

        Status = ServiceRequestStatus.Accepted;
        QuotedPrice = quotedPrice;
        QuotedPriceCurrencyId = quotedPriceCurrencyId;
        ProviderNote = string.IsNullOrWhiteSpace(providerNote) ? null : providerNote.Trim();
        UpdatedAt = utcNow;
    }

    public void Reject(string reason, DateTime utcNow, ServiceOutcomeReason? reasonCode = null)
    {
        EnsureStatus(ServiceRequestStatus.UnderReview, "رفض");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("سبب رفض الطلب مطلوب.");

        Status = ServiceRequestStatus.Rejected;
        RejectionReason = reason.Trim();
        OutcomeReason = reasonCode;
        UpdatedAt = utcNow;
    }

    public void Schedule(DateTime scheduledAt, DateTime utcNow)
    {
        EnsureStatus(ServiceRequestStatus.Accepted, "جدولة");

        if (scheduledAt < utcNow)
            throw new DomainException("لا يمكن جدولة الخدمة في وقت ماضٍ.");

        Status = ServiceRequestStatus.Scheduled;
        ScheduledAt = scheduledAt;
        UpdatedAt = utcNow;
    }

    public void Start(DateTime utcNow)
    {
        EnsureStatus(ServiceRequestStatus.Scheduled, "بدء");

        Status = ServiceRequestStatus.InProgress;
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// Provider says the service was delivered. Allowed from Accepted, Scheduled or InProgress:
    /// Schedule and Start are optional bookkeeping, and forcing them only made providers skip
    /// the outcome. <paramref name="byAdmin"/> also allows UnderReview (admin correcting a request
    /// whose provider never answered).
    /// </summary>
    public void Complete(decimal? finalPrice, int? finalPriceCurrencyId, DateTime utcNow, bool byAdmin = false)
    {
        EnsureCanClose("إتمام", byAdmin);

        if (finalPrice is < 0)
            throw new DomainException("السعر النهائي لا يمكن أن يكون سالباً.");

        if (finalPrice is not null && finalPriceCurrencyId is null)
            throw new DomainException("عملة السعر النهائي مطلوبة عند تحديد سعر.");

        Status = ServiceRequestStatus.Completed;
        FinalPrice = finalPrice;
        FinalPriceCurrencyId = finalPriceCurrencyId;
        CompletedAt = utcNow;
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// Provider says the service did not happen. A reason from the closed list is required;
    /// the note is optional, except for <see cref="ServiceOutcomeReason.Other"/> where it is the
    /// only explanation there is.
    /// </summary>
    public void MarkNotCompleted(ServiceOutcomeReason reason, string? note, DateTime utcNow, bool byAdmin = false)
    {
        EnsureCanClose("إغلاق كغير منجز", byAdmin);

        if (!Enum.IsDefined(reason))
            throw new DomainException("سبب عدم إتمام الخدمة غير معروف.");

        if (reason == ServiceOutcomeReason.Other && string.IsNullOrWhiteSpace(note))
            throw new DomainException("اكتب ملاحظة توضّح السبب عند اختيار «سبب آخر».");

        Status = ServiceRequestStatus.NotCompleted;
        OutcomeReason = reason;
        OutcomeNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        NotCompletedAt = utcNow;
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// The customer's reply after the provider closed the request. One reply per request.
    /// A reason is required whenever the outcome was NotCompleted or the customer disputes it —
    /// those are the cases the admin analyses.
    /// </summary>
    public void RecordRequesterFeedback(
        ServiceRequesterAnswer answer, ServiceRequesterReason? reason, string? note, DateTime utcNow)
    {
        if (Status is not (ServiceRequestStatus.Completed
            or ServiceRequestStatus.Reviewed
            or ServiceRequestStatus.NotCompleted))
        {
            throw new InvalidStateTransitionException(
                $"لا يمكن الرد على نتيجة طلب خدمة في الحالة '{Status}'.");
        }

        if (RequesterAnswer is not null)
            throw new InvalidStateTransitionException("سبق أن أجبت عن نتيجة هذا الطلب.");

        if (!Enum.IsDefined(answer))
            throw new DomainException("الإجابة غير معروفة.");

        var needsReason = Status == ServiceRequestStatus.NotCompleted || answer == ServiceRequesterAnswer.Disputed;
        if (needsReason && reason is null)
            throw new DomainException("اختر سببًا من القائمة.");

        if (reason is { } r && !Enum.IsDefined(r))
            throw new DomainException("السبب غير معروف.");

        if (reason == ServiceRequesterReason.Other && string.IsNullOrWhiteSpace(note))
            throw new DomainException("اكتب ملاحظة توضّح السبب عند اختيار «سبب آخر».");

        RequesterAnswer = answer;
        RequesterReason = reason;
        RequesterFeedbackNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        RequesterFeedbackAt = utcNow;
        UpdatedAt = utcNow;
    }

    /// <summary>
    /// The provider's contact details are shown to the requester only while the request is
    /// alive or was delivered. A rejected, cancelled or not-delivered request ends the
    /// introduction: the provider's number must not outlive a "no".
    /// </summary>
    public bool ProviderContactVisible => Status is
        ServiceRequestStatus.UnderReview or ServiceRequestStatus.Accepted
        or ServiceRequestStatus.Scheduled or ServiceRequestStatus.InProgress
        or ServiceRequestStatus.Completed or ServiceRequestStatus.Reviewed;

    /// <summary>Called once, by AddServiceReviewCommandHandler right after the review is created.</summary>
    public void MarkReviewed(DateTime utcNow)
    {
        EnsureStatus(ServiceRequestStatus.Completed, "تحويل إلى مُقيَّم");

        Status = ServiceRequestStatus.Reviewed;
        UpdatedAt = utcNow;
    }

    public void Cancel(string? reason, DateTime utcNow, ServiceRequesterReason? requesterReason = null)
    {
        if (Status is ServiceRequestStatus.Completed
            or ServiceRequestStatus.Reviewed
            or ServiceRequestStatus.Rejected
            or ServiceRequestStatus.Cancelled
            or ServiceRequestStatus.NotCompleted)
        {
            throw new InvalidStateTransitionException(
                $"لا يمكن إلغاء طلب الخدمة بعد أن أصبحت حالته '{Status}'.");
        }

        Status = ServiceRequestStatus.Cancelled;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        RequesterReason = requesterReason;
        UpdatedAt = utcNow;
    }

    /// <summary>Where the provider may close a request: after accepting it, whether or not it
    /// was scheduled or started. An admin may also close one still under review.</summary>
    private void EnsureCanClose(string action, bool byAdmin)
    {
        var ok = Status is ServiceRequestStatus.Accepted
            or ServiceRequestStatus.Scheduled
            or ServiceRequestStatus.InProgress
            || (byAdmin && Status == ServiceRequestStatus.UnderReview);

        if (!ok)
        {
            throw new InvalidStateTransitionException(
                $"لا يمكن {action} طلب خدمة في الحالة '{Status}'.");
        }
    }

    private void EnsureStatus(ServiceRequestStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new InvalidStateTransitionException(
                $"لا يمكن {action} طلب خدمة في الحالة '{Status}'.");
        }
    }
}
