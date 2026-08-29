using PropertyApi.Domain.ShortStay.Entities;

namespace PropertyApi.Application.ShortStay.Interfaces;

public interface IShortStayReviewRepository
{
    Task AddAsync(ShortStayReview review, CancellationToken ct = default);
    Task<IReadOnlyList<ShortStayReview>> GetByListingIdAsync(Guid listingId, CancellationToken ct = default);
}
