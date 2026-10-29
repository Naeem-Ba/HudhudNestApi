using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Application.ShortStay.Interfaces;

/// <summary>A review as the listing page shows it: the reviewer's display name and photo come with it.</summary>
public sealed record ShortStayReviewRow(
    Guid Id, Guid ReviewerId, string ReviewerName, string? ReviewerImageUrl, int Rating, string? Comment, DateTime CreatedAt);

public interface IShortStayReviewRepository
{
    /// <summary>One page of a listing's reviews, newest first, with the listing's average and total.</summary>
    Task<(IReadOnlyList<ShortStayReviewRow> Items, double Average, int TotalCount)> GetPageByListingIdAsync(
        Guid listingId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Which of these bookings already have a review (so the guest's list can hide the "review" button).</summary>
    Task<IReadOnlySet<Guid>> GetReviewedBookingIdsAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken ct = default);

    Task AddAsync(ShortStayReview review, CancellationToken ct = default);
    Task<IReadOnlyList<ShortStayReview>> GetByListingIdAsync(Guid listingId, CancellationToken ct = default);
}
