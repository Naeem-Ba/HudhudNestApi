using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Application.Common.Paging;

namespace HudhudNestApi.Infrastructure.ShortStay;

public sealed class ShortStayReviewRepository : IShortStayReviewRepository
{
    private readonly AppDbContext _db;
    public ShortStayReviewRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(ShortStayReview review, CancellationToken ct = default)
        => await _db.ShortStayReviews.AddAsync(review, ct);

    public async Task<(IReadOnlyList<ShortStayReviewRow> Items, double Average, int TotalCount)> GetPageByListingIdAsync(
        Guid listingId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.ShortStayReviews.AsNoTracking().Where(r => r.ShortStayListingId == listingId);
        var total = await query.CountAsync(ct);
        var average = total == 0 ? 0d : await query.AverageAsync(r => (double)r.Rating, ct);

        var items = await (
                from review in query
                join account in _db.UserAccounts on review.ReviewerId equals account.Id into accounts
                from account in accounts.DefaultIfEmpty()
                orderby review.CreatedAt descending
                select new ShortStayReviewRow(
                    review.Id,
                    review.ReviewerId,
                    account == null ? "مجهول" : (account.FirstName + " " + account.LastName).Trim(),
                    account == null ? null : account.ProfileImageUrl,
                    review.Rating,
                    review.Comment,
                    review.CreatedAt))
            .SkipPage(page, pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, average, total);
    }

    public async Task<IReadOnlySet<Guid>> GetReviewedBookingIdsAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken ct = default)
    {
        if (bookingIds.Count == 0)
            return new HashSet<Guid>();

        var ids = await _db.ShortStayReviews.AsNoTracking()
            .Where(r => bookingIds.Contains(r.BookingId))
            .Select(r => r.BookingId)
            .ToListAsync(ct);
        return ids.ToHashSet();
    }

    public async Task<IReadOnlyList<ShortStayReview>> GetByListingIdAsync(Guid listingId, CancellationToken ct = default)
        => await _db.ShortStayReviews
            .Where(r => r.ShortStayListingId == listingId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
}
