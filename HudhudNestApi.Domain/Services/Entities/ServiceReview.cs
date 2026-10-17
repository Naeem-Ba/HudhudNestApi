using HudhudNestApi.Domain.Common.Entities;
using HudhudNestApi.Domain.Common.Exceptions;

namespace HudhudNestApi.Domain.Services.Entities;

/// <summary>
/// A rating/comment about how a specific ServiceRequest went. Deliberately its own entity —
/// distinct from PropertyReview (rates a property) and UserRating (rates a person) — because a
/// "verified service review" carries a much stronger claim: it can only exist once the
/// underlying ServiceRequest.Status is Completed and the reviewer is that request's own
/// requester (enforced by AddServiceReviewCommandHandler, not here — this entity has no way to
/// look the request back up). One review per request: ServiceRequestId is unique.
/// </summary>
public sealed class ServiceReview : BaseEntity
{
    private ServiceReview() { }

    public Guid ServiceRequestId { get; private set; }

    /// <summary>Denormalized from the request — lets provider-profile queries avoid a join.</summary>
    public Guid ServiceProviderId { get; private set; }

    public Guid ReviewerId { get; private set; }

    public int Rating { get; private set; }

    public string? Comment { get; private set; }

    /// <summary>An admin hid this review (abuse, wrong target). A hidden review is excluded from
    /// every average and count and from the public list; the row is kept for the audit trail.</summary>
    public bool IsHidden { get; private set; }

    public string? HiddenReason { get; private set; }

    public DateTime? HiddenAt { get; private set; }

    public void Hide(string reason, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("سبب إخفاء التقييم مطلوب.");

        IsHidden = true;
        HiddenReason = reason.Trim();
        HiddenAt = utcNow;
        UpdatedAt = utcNow;
    }

    public void Unhide(DateTime utcNow)
    {
        IsHidden = false;
        HiddenReason = null;
        HiddenAt = null;
        UpdatedAt = utcNow;
    }

    public static ServiceReview Create(
        Guid serviceRequestId,
        Guid serviceProviderId,
        Guid reviewerId,
        int rating,
        string? comment,
        DateTime utcNow)
    {
        if (serviceRequestId == Guid.Empty)
            throw new DomainException("لا يمكن إنشاء تقييم بلا طلب خدمة.");

        if (serviceProviderId == Guid.Empty)
            throw new DomainException("لا يمكن إنشاء تقييم بلا مزوّد خدمة.");

        if (reviewerId == Guid.Empty)
            throw new DomainException("لا يمكن إنشاء تقييم بلا مُقيِّم.");

        if (rating is < 1 or > 5)
            throw new DomainException("التقييم يجب أن يكون بين 1 و5.");

        return new ServiceReview
        {
            ServiceRequestId = serviceRequestId,
            ServiceProviderId = serviceProviderId,
            ReviewerId = reviewerId,
            Rating = rating,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            CreatedAt = utcNow,
            UpdatedAt = utcNow,
        };
    }
}
