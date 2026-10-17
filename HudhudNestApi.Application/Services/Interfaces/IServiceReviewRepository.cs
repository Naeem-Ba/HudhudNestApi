using HudhudNestApi.Domain.Services.Entities;

namespace HudhudNestApi.Application.Services.Interfaces;

public interface IServiceReviewRepository
{
    Task AddAsync(ServiceReview review, CancellationToken ct = default);

    Task<bool> ExistsForRequestAsync(Guid serviceRequestId, CancellationToken ct = default);

    Task<IReadOnlyList<ServiceReview>> GetByProviderIdAsync(
        Guid serviceProviderId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Visible (not hidden) rating summary per offering, through the request each review
    /// belongs to. Offerings without a review are absent from the result.</summary>
    Task<IReadOnlyDictionary<Guid, (double? Average, int Count)>> GetOfferingRatingSummariesAsync(
        IReadOnlyCollection<Guid> offeringIds, CancellationToken ct = default);

    Task<ServiceReview?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>The review of one request, hidden or not (admin view).</summary>
    Task<ServiceReview?> GetByRequestIdAsync(Guid serviceRequestId, CancellationToken ct = default);

    /// <summary>Returns (average, count) in one query — used on the provider profile card so
    /// it never has to load every review just to show "4.6 (12)".</summary>
    Task<(double? Average, int Count)> GetRatingSummaryAsync(
        Guid serviceProviderId, CancellationToken ct = default);
}
