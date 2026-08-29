using PropertyApi.Domain.Common.Entities;
using PropertyApi.Domain.Common.Exceptions;

namespace PropertyApi.Domain.ShortStay.Entities;

/// <summary>
/// Guest review of a completed stay. Eligibility (Booking.Status == Completed, reviewer ==
/// Booking.GuestId, one review per booking) is enforced in AddShortStayReviewCommandHandler,
/// mirroring AddReviewCommandHandler's pattern for PropertyReview — mirrored pattern, not a
/// shared base class, because the eligibility source (a completed Booking) differs from a
/// completed Visit.
/// </summary>
public sealed class ShortStayReview : BaseEntity
{
    public Guid BookingId { get; private set; }
    public Guid ReviewerId { get; private set; }
    public Guid ShortStayListingId { get; private set; }
    public int Rating { get; private set; }
    public string? Comment { get; private set; }

    private ShortStayReview() { }

    public static ShortStayReview Create(Guid bookingId, Guid reviewerId, Guid shortStayListingId, int rating, string? comment)
    {
        if (rating is < 1 or > 5)
            throw new DomainException("التقييم يجب أن يكون بين 1 و 5.");

        return new ShortStayReview
        {
            BookingId = bookingId,
            ReviewerId = reviewerId,
            ShortStayListingId = shortStayListingId,
            Rating = rating,
            Comment = comment?.Trim(),
        };
    }
}
