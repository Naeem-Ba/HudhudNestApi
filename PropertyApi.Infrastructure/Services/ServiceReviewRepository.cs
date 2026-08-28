using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Services.Interfaces;
using PropertyApi.Domain.Services.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Services;

public sealed class ServiceReviewRepository : IServiceReviewRepository
{
    private readonly AppDbContext _db;
    public ServiceReviewRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(ServiceReview review, CancellationToken ct = default)
        => await _db.ServiceReviews.AddAsync(review, ct);

    public async Task<bool> ExistsForRequestAsync(Guid serviceRequestId, CancellationToken ct = default)
        => await _db.ServiceReviews.AnyAsync(r => r.ServiceRequestId == serviceRequestId, ct);

    public async Task<IReadOnlyList<ServiceReview>> GetByProviderIdAsync(
        Guid serviceProviderId, int page, int pageSize, CancellationToken ct = default)
        => await _db.ServiceReviews
            .Where(r => r.ServiceProviderId == serviceProviderId)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

    public async Task<(double? Average, int Count)> GetRatingSummaryAsync(
        Guid serviceProviderId, CancellationToken ct = default)
    {
        var ratings = await _db.ServiceReviews
            .Where(r => r.ServiceProviderId == serviceProviderId)
            .Select(r => r.Rating)
            .ToListAsync(ct);

        if (ratings.Count == 0)
            return (null, 0);

        return (ratings.Average(), ratings.Count);
    }
}
