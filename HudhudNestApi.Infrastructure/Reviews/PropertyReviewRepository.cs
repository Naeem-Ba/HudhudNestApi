using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Reviews.Interfaces;
using HudhudNestApi.Domain.Reviews.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Reviews;

public sealed class PropertyReviewRepository : IPropertyReviewRepository
{
    private readonly AppDbContext _db;

    public PropertyReviewRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(PropertyReview review, CancellationToken ct = default)
        => await _db.PropertyReviews.AddAsync(review, ct);

    public async Task<PropertyReview?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.PropertyReviews.FindAsync([id], ct);

    public async Task<bool> HasUserReviewedAsync(
        Guid propertyId,
        Guid reviewerId,
        CancellationToken ct = default)
        => await _db.PropertyReviews.AnyAsync(
            r => r.PropertyId == propertyId && r.ReviewerId == reviewerId,
            ct);

    public async Task<(IReadOnlyList<PropertyReview> Reviews, double Average, int TotalCount)> GetByPropertyIdAsync(
        Guid propertyId,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var query = _db.PropertyReviews
            .AsNoTracking()
            .Where(r => r.PropertyId == propertyId);

        var totalCount = await query.CountAsync(ct);
        var average = await query
            .Select(r => (double?)r.Rating)
            .AverageAsync(ct) ?? 0d;

        var reviews = await query
            .Include(r => r.Reviewer)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (reviews.AsReadOnly(), average, totalCount);
    }

    public Task DeleteAsync(PropertyReview review, CancellationToken ct = default)
    {
        _db.PropertyReviews.Remove(review);
        return Task.CompletedTask;
    }
}
