using HudhudNestApi.Domain.ShortStay.Entities;

namespace HudhudNestApi.Application.ShortStay.Interfaces;

public interface IShortStayReviewRepository
{
    Task AddAsync(ShortStayReview review, CancellationToken ct = default);
    Task<IReadOnlyList<ShortStayReview>> GetByListingIdAsync(Guid listingId, CancellationToken ct = default);
}
