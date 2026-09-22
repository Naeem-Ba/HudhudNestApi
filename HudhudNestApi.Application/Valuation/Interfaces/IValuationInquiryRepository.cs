using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Domain.Valuation.Entities;
using HudhudNestApi.Domain.Valuation.Enums;

namespace HudhudNestApi.Application.Valuation.Interfaces;

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
    /// Remediation M4 — non-terminal inquiries (Pending/MatchedFromListings/
    /// AwaitingOfficeResponses) whose CreatedAt+<see cref="ValuationInquiry.ReminderWindow"/>
    /// (18h) has passed and which have not yet had a reminder successfully delivered
    /// (<see cref="ValuationInquiry.ReminderSentAt"/> is null). Same ordering/batching
    /// reasoning as <see cref="GetDueForExpiryAsync"/>. Tracked — the caller stamps
    /// ReminderSentAt on success and saves.
    /// </summary>
    Task<IReadOnlyList<ValuationInquiry>> GetDueForReminderAsync(
        DateTime utcNow,
        int batchSize,
        CancellationToken ct = default);

    /// <summary>
    /// Remediation M3 — Expired inquiries with a real requester whose
    /// ValuationInquiryExpired notification has not yet been confirmed delivered
    /// (<see cref="ValuationInquiry.ExpiryNotifiedAt"/> is null) — the retry queue for a
    /// notification attempt that failed (or was never attempted, e.g. a first sweep tick that
    /// crashed after Expire() committed but before the notification loop ran). Excludes
    /// anonymous inquiries at the database level (RequesterId is null) rather than fetching
    /// rows this phase can never act on.
    /// </summary>
    Task<IReadOnlyList<ValuationInquiry>> GetExpiredAwaitingNotificationAsync(
        int batchSize,
        CancellationToken ct = default);

    /// <summary>
    /// Remediation M3/H2 — Completed inquiries with a real requester whose
    /// ValuationResultReady notification has not yet been confirmed delivered
    /// (<see cref="ValuationInquiry.ResultReadyNotifiedAt"/> is null) — the retry queue for
    /// SubmitOfficeResponseCommandHandler's own best-effort, synchronous notification attempt
    /// when it fails.
    /// </summary>
    Task<IReadOnlyList<ValuationInquiry>> GetCompletedAwaitingResultNotificationAsync(
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
