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
    /// that no <see cref="DistributionRun"/> has ever evaluated — the reconciliation sweep's work list.
    /// Oldest first, so a backlog drains in order.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetPublishedPropertyIdsWithoutRunAsync(
        DateTime publishedSinceUtc, DateTime publishedBeforeUtc, int take, CancellationToken ct = default);

    void Update(DistributionRun run);
}
