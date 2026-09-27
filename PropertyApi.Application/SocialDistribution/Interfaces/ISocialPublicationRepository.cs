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
    /// Publications stuck in <see cref="Domain.SocialDistribution.Enums.SocialPublicationStatus.Publishing"/>
    /// past their <see cref="SocialPublication.LeaseUntil"/> — the lease reaper's work list (see
    /// <see cref="SocialPublication.ReleaseExpiredLease"/>). Always a tiny set in a healthy system;
    /// non-empty only after a worker crash/restart interrupted an in-flight publish attempt.
    /// </summary>
    Task<IReadOnlyList<SocialPublication>> GetPublishingWithExpiredLeaseAsync(DateTime utcNow, int take, CancellationToken ct = default);

    /// <summary>
    /// Phase 4 idempotency guard (spec §12): true when a rule-engine-created (i.e.
    /// DistributionRuleId is not null), non-Cancelled publication already exists for this exact
    /// (property, account) pair. <see cref="Services.DistributionEngine"/> checks this before
    /// creating a new one, so re-running distribution for the same property/account never
    /// produces two simultaneously "live" publications — a Cancelled one frees the slot, and a
    /// manually-created publication (DistributionRuleId null) never blocks the engine.
    /// </summary>
    Task<bool> ExistsActiveForPropertyAndAccountAsync(Guid propertyId, Guid socialAccountId, CancellationToken ct = default);

    /// <summary>
    /// Every currently-live (Published) publication for a property — the set
    /// <c>PropertyStatusChangedDistributionHandler</c> (Phase 11) evaluates a lifecycle action
    /// against. Deliberately Published-only: a Draft/Queued/Retrying/Failed/Cancelled publication
    /// was never actually posted, so there is nothing external to update/comment-on/delete.
    /// </summary>
    Task<IReadOnlyList<SocialPublication>> GetActiveForPropertyAsync(Guid propertyId, CancellationToken ct = default);

    /// <summary>Phase 10 spec §6 dashboard tiles — a handful of real aggregate counts/rates, computed by the database, never estimated.</summary>
    Task<SocialDashboardSummaryDto> GetDashboardSummaryAsync(DateTime utcNow, CancellationToken ct = default);

    void Update(SocialPublication publication);
}
