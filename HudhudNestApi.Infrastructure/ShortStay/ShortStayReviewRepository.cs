using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.ShortStay;

public sealed class ShortStayReviewRepository : IShortStayReviewRepository
{
    private readonly AppDbContext _db;
    public ShortStayReviewRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(ShortStayReview review, CancellationToken ct = default)
        => await _db.ShortStayReviews.AddAsync(review, ct);

    public async Task<IReadOnlyList<ShortStayReview>> GetByListingIdAsync(Guid listingId, CancellationToken ct = default)
        => await _db.ShortStayReviews
            .Where(r => r.ShortStayListingId == listingId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);
}
