using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Domain.ShortStay.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.ShortStay;

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
