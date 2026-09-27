using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.SocialDistribution.Entities;

namespace PropertyApi.Application.SocialDistribution.Interfaces;

public interface IDistributionRunRepository
{
    Task AddAsync(DistributionRun run, CancellationToken ct = default);

    Task<DistributionRun?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<DistributionRun>> GetByPropertyIdAsync(Guid propertyId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// Public, unexpired, available listings published inside <c>[publishedSinceUtc, publishedBeforeUtc]</c>
    /// that have never produced an actual publication — the reconciliation sweep's work list.
    /// Oldest first, so a backlog drains in order.
    ///
    /// Deliberately "no run created any publication yet", not "no run has ever evaluated this
    /// property": a listing is created with zero photos (image upload is a separate step in this
    /// codebase's flow) and is genuinely ineligible on its first pass
    /// (<c>SocialDistributionEligibilityOptions.MinImageCount</c>). Excluding it forever after that
    /// one attempt would mean a listing whose owner adds photos minutes later is never
    /// automatically distributed at all — so it keeps being retried, bounded only by
    /// <c>publishedSinceUtc</c>'s lookback window, until some run actually creates at least one
    /// publication.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetPublishedPropertyIdsWithoutRunAsync(
        DateTime publishedSinceUtc, DateTime publishedBeforeUtc, int take, CancellationToken ct = default);

    void Update(DistributionRun run);
}
