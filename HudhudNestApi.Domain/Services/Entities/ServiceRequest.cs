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

    public void Reject(string reason, DateTime utcNow)
    {
        EnsureStatus(ServiceRequestStatus.UnderReview, "رفض");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("سبب رفض الطلب مطلوب.");

        Status = ServiceRequestStatus.Rejected;
        RejectionReason = reason.Trim();
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

    public void Complete(decimal? finalPrice, int? finalPriceCurrencyId, DateTime utcNow)
    {
        EnsureStatus(ServiceRequestStatus.InProgress, "إتمام");

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

    /// <summary>Called once, by AddServiceReviewCommandHandler right after the review is created.</summary>
    public void MarkReviewed(DateTime utcNow)
    {
        EnsureStatus(ServiceRequestStatus.Completed, "تحويل إلى مُقيَّم");

        Status = ServiceRequestStatus.Reviewed;
        UpdatedAt = utcNow;
    }

    public void Cancel(string? reason, DateTime utcNow)
    {
        if (Status is ServiceRequestStatus.Completed
            or ServiceRequestStatus.Reviewed
            or ServiceRequestStatus.Rejected
            or ServiceRequestStatus.Cancelled)
        {
            throw new InvalidStateTransitionException(
                $"لا يمكن إلغاء طلب الخدمة بعد أن أصبحت حالته '{Status}'.");
        }

        Status = ServiceRequestStatus.Cancelled;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        UpdatedAt = utcNow;
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
