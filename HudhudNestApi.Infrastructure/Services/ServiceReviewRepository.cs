using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Application.Common.Paging;

namespace HudhudNestApi.Infrastructure.Services;

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
            .Where(r => r.ServiceProviderId == serviceProviderId && !r.IsHidden)
            .OrderByDescending(r => r.CreatedAt)
            .SkipPage(page, pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, (double? Average, int Count)>> GetOfferingRatingSummariesAsync(
        IReadOnlyCollection<Guid> offeringIds, CancellationToken ct = default)
    {
        if (offeringIds.Count == 0)
            return new Dictionary<Guid, (double? Average, int Count)>();

        var rows = await (
            from review in _db.ServiceReviews
            join request in _db.ServiceRequests on review.ServiceRequestId equals request.Id
            where !review.IsHidden && offeringIds.Contains(request.ServiceOfferingId)
            group review by request.ServiceOfferingId into g
            select new { OfferingId = g.Key, Average = g.Average(x => (double)x.Rating), Count = g.Count() })
            .ToListAsync(ct);

        return rows.ToDictionary(x => x.OfferingId, x => ((double?)x.Average, x.Count));
    }

    public async Task<ServiceReview?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.ServiceReviews.FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<ServiceReview?> GetByRequestIdAsync(Guid serviceRequestId, CancellationToken ct = default)
        => await _db.ServiceReviews.FirstOrDefaultAsync(r => r.ServiceRequestId == serviceRequestId, ct);

    public async Task<(double? Average, int Count)> GetRatingSummaryAsync(
        Guid serviceProviderId, CancellationToken ct = default)
    {
        var ratings = await _db.ServiceReviews
            .Where(r => r.ServiceProviderId == serviceProviderId && !r.IsHidden)
            .Select(r => r.Rating)
            .ToListAsync(ct);

        if (ratings.Count == 0)
            return (null, 0);

        return (ratings.Average(), ratings.Count);
    }
}
