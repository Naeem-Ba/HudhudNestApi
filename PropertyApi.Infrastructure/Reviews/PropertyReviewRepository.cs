using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Reviews.Interfaces;
using PropertyApi.Domain.Reviews.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Reviews;

public sealed class PropertyReviewRepository : IPropertyReviewRepository
{
    private readonly AppDbContext _db;
    public PropertyReviewRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(PropertyReview review, CancellationToken ct = default)
        => await _db.PropertyReviews.AddAsync(review, ct);

    public async Task<PropertyReview?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.PropertyReviews.FindAsync([id], ct);

    public async Task<bool> HasUserReviewedAsync(
        Guid propertyId, Guid reviewerId, CancellationToken ct = default)
        => await _db.PropertyReviews
            .AnyAsync(r =>
                r.PropertyId == propertyId &&
                r.ReviewerId == reviewerId, ct);

    public async Task<(IReadOnlyList<PropertyReview> Reviews, double Average)>
        GetByPropertyIdAsync(Guid propertyId, CancellationToken ct = default)
    {
        var reviews = await _db.PropertyReviews
            .Include(r => r.Reviewer)
            .Where(r => r.PropertyId == propertyId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        double avg = reviews.Count > 0
            ? reviews.Average(r => r.Rating)
            : 0;

        return (reviews.AsReadOnly(), avg);
    }

    public async Task DeleteAsync(PropertyReview review, CancellationToken ct = default)
    {
        _db.PropertyReviews.Remove(review);
        await Task.CompletedTask;
    }
}