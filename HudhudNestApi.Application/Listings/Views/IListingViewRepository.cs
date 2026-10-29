using HudhudNestApi.Domain.Listings.Enums;

namespace HudhudNestApi.Application.Listings.Views;

public interface IListingViewRepository
{
    /// <summary>Owner, agency, title, public visibility and current total of a listing; null when it does not exist (or is deleted).</summary>
    Task<ListingViewTarget?> GetTargetAsync(ListingKind kind, Guid listingId, CancellationToken ct = default);

    /// <summary>Current totals for many listings of one kind (listings never viewed are absent from the result).</summary>
    Task<IReadOnlyDictionary<Guid, long>> GetCountsAsync(ListingKind kind, IReadOnlyCollection<Guid> listingIds, CancellationToken ct = default);

    /// <summary>Agency the user works for, or null.</summary>
    Task<Guid?> GetUserAgencyIdAsync(Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Records one view and adds one to the listing total, atomically and exactly once per
    /// (listing, viewer, bucket): the ledger insert and the increment run in one transaction, and the
    /// insert is <c>ON CONFLICT DO NOTHING</c>, so concurrent or repeated calls for the same key add one.
    /// Returns whether this call counted, and the total afterwards.
    /// </summary>
    Task<(bool Counted, long TotalViews)> TryRecordAsync(
        ListingKind kind, Guid listingId, string viewerKeyHash, long bucket, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Per-listing totals and windowed counts for the listings of one owner.</summary>
    Task<IReadOnlyList<ListingViewStatsRow>> GetOwnerStatsAsync(Guid ownerId, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Same for every listing published under an agency.</summary>
    Task<IReadOnlyList<ListingViewStatsRow>> GetAgencyStatsAsync(Guid agencyId, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Views per UTC day for one listing, oldest first, zero-filled days omitted (the handler fills them).</summary>
    Task<IReadOnlyDictionary<DateOnly, int>> GetDailyViewsAsync(
        ListingKind kind, Guid listingId, DateTime fromUtc, CancellationToken ct = default);

    /// <summary>Deletes ledger rows older than the cutoff, at most <paramref name="batchSize"/>; returns the number removed.</summary>
    Task<int> PurgeOlderThanAsync(DateTime cutoffUtc, int batchSize, CancellationToken ct = default);
}

/// <summary>Policy knobs and viewer hashing for the view counter (configuration + secret live in Infrastructure).</summary>
public interface IListingViewPolicy
{
    /// <summary>Length of one dedupe window: a viewer adds at most one view per listing per window.</summary>
    TimeSpan Cooldown { get; }

    /// <summary>How long ledger rows (and so the history stats) are kept.</summary>
    int RetentionDays { get; }

    /// <summary>Window number for an instant: <c>floor(unixSeconds / cooldownSeconds)</c>.</summary>
    long BucketFor(DateTime utc);

    /// <summary>Keyed digest of an identity string (<c>u:{userId}</c>, <c>v:{token}</c> or <c>f:{fingerprint}</c>), 64 hex chars.</summary>
    string HashViewer(string identity);
}
