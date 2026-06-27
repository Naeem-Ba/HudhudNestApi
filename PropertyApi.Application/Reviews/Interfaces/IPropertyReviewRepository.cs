using PropertyApi.Domain.Reviews.Entities;

namespace PropertyApi.Application.Reviews.Interfaces;

public interface IPropertyReviewRepository
{
    Task AddAsync(PropertyReview review, CancellationToken ct = default);
    Task<PropertyReview?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> HasUserReviewedAsync(Guid propertyId, Guid reviewerId, CancellationToken ct = default);

    Task<(IReadOnlyList<PropertyReview> Reviews, double Average, int TotalCount)> GetByPropertyIdAsync(
        Guid propertyId,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task DeleteAsync(PropertyReview review, CancellationToken ct = default);
}
