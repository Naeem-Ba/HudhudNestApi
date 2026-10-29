using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Moderation.Entities;
using HudhudNestApi.Domain.Moderation.Enums;

namespace HudhudNestApi.Application.Moderation;

public interface IListingReportRepository
{
    Task AddAsync(ListingReport report, CancellationToken ct = default);

    Task AddEventAsync(ListingReportEvent entry, CancellationToken ct = default);

    /// <summary>Tracked, for changes.</summary>
    Task<ListingReport?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<ListingReport?> FindByClientRequestAsync(Guid reporterUserId, Guid clientRequestId, CancellationToken ct = default);

    Task<ListingReport?> FindOpenAsync(Guid reporterUserId, Guid propertyId, CancellationToken ct = default);

    /// <summary>When this account's latest closed report on the listing for this reason was closed, if any.</summary>
    Task<DateTime?> LastClosedAtAsync(Guid reporterUserId, Guid propertyId, ListingReportReason reason, CancellationToken ct = default);

    Task<int> CountByReporterSinceAsync(Guid reporterUserId, DateTime sinceUtc, CancellationToken ct = default);

    /// <summary>Open reports on a listing (tracked), for the "several accounts" escalation.</summary>
    Task<IReadOnlyList<ListingReport>> ListOpenForPropertyAsync(Guid propertyId, CancellationToken ct = default);

    Task<(IReadOnlyList<ListingReport> Items, int Total)> ListByReporterAsync(
        Guid reporterUserId, int page, int pageSize, CancellationToken ct = default);

    Task<(IReadOnlyList<AdminListingReportRow> Items, int Total)> SearchAsync(
        ListingReportFilter filter, IReadOnlyDictionary<ListingReportPriority, DateTime> overdueCutoffs, CancellationToken ct = default);

    Task<ListingReportStatsRow> GetStatsAsync(
        ListingReportFilter filter, IReadOnlyDictionary<ListingReportPriority, DateTime> overdueCutoffs, CancellationToken ct = default);

    Task<IReadOnlyList<ListingReportEvent>> ListEventsAsync(Guid reportId, bool includeInternal, CancellationToken ct = default);

    /// <summary>Other reports on the same listing, newest first (admin view).</summary>
    Task<IReadOnlyList<ListingReport>> ListForPropertyAsync(Guid propertyId, Guid exceptReportId, int take, CancellationToken ct = default);

    /// <summary>The listing if a visitor can see it right now (the same rule as the public detail page), else null.</summary>
    Task<ReportableListing?> GetReportableListingAsync(Guid propertyId, DateTime utcNow, CancellationToken ct = default);

    /// <summary>The listing as it is now for the admin, including when it was deleted or taken down. Null only if the row is gone.</summary>
    Task<ListingSnapshot?> GetListingSnapshotAsync(Guid propertyId, DateTime utcNow, CancellationToken ct = default);

    /// <summary>How many reports this account sent, and how many of those were dismissed (a credibility signal for the admin).</summary>
    Task<(int Total, int Dismissed)> GetReporterHistoryAsync(Guid reporterUserId, CancellationToken ct = default);

    /// <summary>Active accounts holding the Admin role: who a report can be assigned to and who is told about new reports.</summary>
    Task<IReadOnlyList<UserLabel>> ListAdminsAsync(CancellationToken ct = default);

    Task<bool> IsActiveAdminAsync(Guid userId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, string>> GetUserLabelsAsync(IEnumerable<Guid> userIds, CancellationToken ct = default);
}

public enum ListingReportSort
{
    Newest = 1,
    Oldest = 2,
    Priority = 3,
    RecentlyUpdated = 4,
}

public sealed record ListingReportFilter(
    string? Search,
    ListingReportStatus? Status,
    bool OpenOnly,
    ListingReportReason? Reason,
    ListingReportPriority? Priority,
    Guid? AssignedAdminId,
    bool UnassignedOnly,
    Guid? PropertyId,
    DateTime? FromUtc,
    DateTime? ToUtc,
    bool OverdueOnly,
    ListingReportSort Sort,
    int Page,
    int PageSize);

public sealed record ReportableListing(Guid Id, string Title, Guid OwnerId);

public sealed record ListingSnapshot(
    Guid Id,
    string Title,
    Guid OwnerId,
    ListingType ListingType,
    string Status,
    bool IsPublished,
    bool IsTakenDown,
    string? TakenDownReason,
    bool IsDeleted,
    bool IsPubliclyVisible,
    decimal? Price,
    string CurrencyCode,
    string? City,
    DateTime? PublishedAt,
    DateTime? ExpiresAt);

public sealed record UserLabel(Guid Id, string Name);

public sealed record AdminListingReportRow(
    ListingReport Report,
    string? CurrentListingTitle,
    ListingType? ListingType,
    bool ListingDeleted,
    bool ListingTakenDown);

public sealed record ListingReportStatsRow(
    int Total,
    int New,
    int UnderReview,
    int WaitingForInformation,
    int ActionRequired,
    int Resolved,
    int Dismissed,
    int HighPriorityOpen,
    int Overdue,
    double? AverageResolutionHours,
    int ResolvedSampleSize);
