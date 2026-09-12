using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;

namespace PropertyApi.Application.Valuation.Interfaces;

/// <summary>
/// Persistence for <see cref="ValuationInquiry"/>. Stage 4 (OfficeMatchingService) built its
/// matching decision entirely in-memory because no persistence existed yet for this module —
/// this is that deferred persistence layer, added now because Stage 5's SLA sweep is the first
/// thing that actually needs to load real, previously-created inquiries back out of a store.
///
/// Note: no command/handler in this codebase yet creates and persists a ValuationInquiry from
/// a live request (GetComparableListingsQueryHandler takes the inquiry's field values directly
/// rather than an id to load, exactly because nothing could load one yet — see its own doc
/// comment). That "create and persist" orchestration is out of scope here too; Stage 5 only
/// needs enough repository surface for its expiry sweep to find and update rows that some
/// future orchestrator will create.
/// </summary>
public interface IValuationInquiryRepository
{
    Task<ValuationInquiry?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(ValuationInquiry inquiry, CancellationToken ct = default);

    void Update(ValuationInquiry inquiry);

    /// <summary>
    /// Inquiries still in a non-terminal status (Pending/MatchedFromListings/
    /// AwaitingOfficeResponses) whose 24h <see cref="ValuationInquiry.ExpiresAt"/> has already
    /// passed — Stage 5's own "never left indefinitely pending" rule. Ordered by ExpiresAt so
    /// the longest-overdue inquiries are always processed first if a backlog exists, and capped
    /// by <paramref name="batchSize"/> so a large backlog drains over several sweeps instead of
    /// one very long-running one (same reasoning ListingExpiryHostedService's MaxItemsPerPhase
    /// already applies). Tracked (not AsNoTracking) — the caller expires each one and saves.
    /// </summary>
    Task<IReadOnlyList<ValuationInquiry>> GetDueForExpiryAsync(
        DateTime utcNow,
        int batchSize,
        CancellationToken ct = default);

    /// <summary>
    /// Stage 8 (Admin Dashboard) — every inquiry, optionally filtered by
    /// <see cref="ValuationInquiryStatus"/>, newest first. Database-side Where/OrderBy/
    /// Skip/Take (never "load everything then filter in memory" — this module's own rule 8)
    /// since this list is unbounded, unlike GetMyAgencyValuationInquiries which is naturally
    /// capped to one agency's own invitations. AsNoTracking: admin read-only view.
    /// </summary>
    Task<PagedResult<ValuationInquiry>> GetPagedAsync(
        ValuationInquiryStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default);
}
