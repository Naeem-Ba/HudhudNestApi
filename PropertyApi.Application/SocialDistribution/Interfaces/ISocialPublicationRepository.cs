using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Domain.SocialDistribution.Entities;

namespace PropertyApi.Application.SocialDistribution.Interfaces;

public interface ISocialPublicationRepository
{
    Task AddAsync(SocialPublication publication, CancellationToken ct = default);

    /// <summary>Includes Content and the target SocialAccount — the shape every command handler needs.</summary>
    Task<SocialPublication?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PagedResult<SocialPublication>> GetPagedAsync(SocialPublicationFilterDto filter, CancellationToken ct = default);

    /// <summary>Queued publications whose ScheduledAt has arrived (or is null), oldest first, capped at <paramref name="take"/> — feeds the dispatch worker.</summary>
    Task<IReadOnlyList<SocialPublication>> GetDueToPublishAsync(DateTime utcNow, int take, CancellationToken ct = default);

    /// <summary>Retrying publications whose NextRetryAt has arrived, oldest first, capped at <paramref name="take"/>.</summary>
    Task<IReadOnlyList<SocialPublication>> GetDueForRetryAsync(DateTime utcNow, int take, CancellationToken ct = default);

    /// <summary>
    /// Phase 4 idempotency guard (spec §12): true when a rule-engine-created (i.e.
    /// DistributionRuleId is not null), non-Cancelled publication already exists for this exact
    /// (property, account) pair. <see cref="Services.DistributionEngine"/> checks this before
    /// creating a new one, so re-running distribution for the same property/account never
    /// produces two simultaneously "live" publications — a Cancelled one frees the slot, and a
    /// manually-created publication (DistributionRuleId null) never blocks the engine.
    /// </summary>
    Task<bool> ExistsActiveForPropertyAndAccountAsync(Guid propertyId, Guid socialAccountId, CancellationToken ct = default);

    void Update(SocialPublication publication);
}
