using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Moderation;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Moderation.Entities;
using HudhudNestApi.Domain.Moderation.Enums;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Application.Common.Paging;

namespace HudhudNestApi.Infrastructure.Moderation;

public sealed class ListingReportRepository : IListingReportRepository
{
    private const int ResolutionSampleLimit = 2000;

    private static readonly ListingReportStatus[] OpenStatuses =
    [
        ListingReportStatus.New,
        ListingReportStatus.UnderReview,
        ListingReportStatus.WaitingForInformation,
        ListingReportStatus.ActionRequired,
    ];

    private static readonly ListingReportStatus[] ClosedStatuses =
    [
        ListingReportStatus.Resolved,
        ListingReportStatus.Dismissed,
    ];

    private readonly AppDbContext _db;
    public ListingReportRepository(AppDbContext db) => _db = db;

    public async Task AddAsync(ListingReport report, CancellationToken ct = default)
        => await _db.ListingReports.AddAsync(report, ct);

    public async Task AddEventAsync(ListingReportEvent entry, CancellationToken ct = default)
        => await _db.ListingReportEvents.AddAsync(entry, ct);

    public Task<ListingReport?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.ListingReports.FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<ListingReport?> FindByClientRequestAsync(Guid reporterUserId, Guid clientRequestId, CancellationToken ct = default)
        => _db.ListingReports.FirstOrDefaultAsync(r => r.ReporterUserId == reporterUserId && r.ClientRequestId == clientRequestId, ct);

    public Task<ListingReport?> FindOpenAsync(Guid reporterUserId, Guid propertyId, CancellationToken ct = default)
        => _db.ListingReports.FirstOrDefaultAsync(r =>
            r.ReporterUserId == reporterUserId && r.PropertyId == propertyId && OpenStatuses.Contains(r.Status), ct);

    public Task<DateTime?> LastClosedAtAsync(Guid reporterUserId, Guid propertyId, ListingReportReason reason, CancellationToken ct = default)
        => _db.ListingReports.AsNoTracking()
            .Where(r => r.ReporterUserId == reporterUserId && r.PropertyId == propertyId && r.Reason == reason
                        && ClosedStatuses.Contains(r.Status))
            .MaxAsync(r => r.ResolvedAt, ct);

    public Task<int> CountByReporterSinceAsync(Guid reporterUserId, DateTime sinceUtc, CancellationToken ct = default)
        => _db.ListingReports.CountAsync(r => r.ReporterUserId == reporterUserId && r.CreatedAt >= sinceUtc, ct);

    public async Task<IReadOnlyList<ListingReport>> ListOpenForPropertyAsync(Guid propertyId, CancellationToken ct = default)
        => await _db.ListingReports
            .Where(r => r.PropertyId == propertyId && OpenStatuses.Contains(r.Status))
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<ListingReport> Items, int Total)> ListByReporterAsync(
        Guid reporterUserId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.ListingReports.AsNoTracking().Where(r => r.ReporterUserId == reporterUserId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(r => r.CreatedAt)
            .SkipPage(page, pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    public async Task<(IReadOnlyList<AdminListingReportRow> Items, int Total)> SearchAsync(
        ListingReportFilter filter, IReadOnlyDictionary<ListingReportPriority, DateTime> overdueCutoffs, CancellationToken ct = default)
    {
        var query = ApplyFilter(Joined(), filter, overdueCutoffs, includeStatusFilters: true);

        var total = await query.CountAsync(ct);

        query = filter.Sort switch
        {
            ListingReportSort.Oldest => query.OrderBy(x => x.Report.CreatedAt),
            ListingReportSort.Priority => query.OrderByDescending(x => x.Report.Priority).ThenBy(x => x.Report.CreatedAt),
            ListingReportSort.RecentlyUpdated => query.OrderByDescending(x => x.Report.UpdatedAt),
            _ => query.OrderByDescending(x => x.Report.CreatedAt),
        };

        var rows = await query
            .SkipPage(filter.Page, filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(ct);

        return (rows.Select(x => new AdminListingReportRow(
            x.Report, x.Title, x.ListingType, x.Deleted ?? true, x.TakenDown ?? false)).ToList(), total);
    }

    public async Task<ListingReportStatsRow> GetStatsAsync(
        ListingReportFilter filter, IReadOnlyDictionary<ListingReportPriority, DateTime> overdueCutoffs, CancellationToken ct = default)
    {
        // Status, open-only and overdue are what the cards themselves count, so they are not applied here;
        // the other filters (dates, reason, priority, assignee, listing, search) are.
        var reports = ApplyFilter(Joined(), filter, overdueCutoffs, includeStatusFilters: false).Select(x => x.Report);

        var byStatus = await reports
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int Count(ListingReportStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

        var highOpen = await reports.CountAsync(r =>
            OpenStatuses.Contains(r.Status) && r.Priority >= ListingReportPriority.High, ct);
        var overdue = await reports.Where(OverduePredicate(overdueCutoffs)).CountAsync(ct);

        var durations = await reports
            .Where(r => ClosedStatuses.Contains(r.Status) && r.ResolvedAt != null)
            .OrderByDescending(r => r.ResolvedAt)
            .Take(ResolutionSampleLimit)
            .Select(r => new { r.CreatedAt, ResolvedAt = r.ResolvedAt!.Value })
            .ToListAsync(ct);

        double? average = durations.Count == 0
            ? null
            : durations.Average(d => Math.Max(0, (d.ResolvedAt - d.CreatedAt).TotalHours));

        return new ListingReportStatsRow(
            byStatus.Sum(x => x.Count),
            Count(ListingReportStatus.New),
            Count(ListingReportStatus.UnderReview),
            Count(ListingReportStatus.WaitingForInformation),
            Count(ListingReportStatus.ActionRequired),
            Count(ListingReportStatus.Resolved),
            Count(ListingReportStatus.Dismissed),
            highOpen,
            overdue,
            average,
            durations.Count);
    }

    public async Task<IReadOnlyList<ListingReportEvent>> ListEventsAsync(Guid reportId, bool includeInternal, CancellationToken ct = default)
        => await _db.ListingReportEvents.AsNoTracking()
            .Where(e => e.ReportId == reportId && (includeInternal || !e.IsInternal))
            .OrderBy(e => e.CreatedAt)
            .ThenBy(e => e.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ListingReport>> ListForPropertyAsync(
        Guid propertyId, Guid exceptReportId, int take, CancellationToken ct = default)
        => await _db.ListingReports.AsNoTracking()
            .Where(r => r.PropertyId == propertyId && r.Id != exceptReportId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

    public Task<ReportableListing?> GetReportableListingAsync(Guid propertyId, DateTime utcNow, CancellationToken ct = default)
        => _db.Properties.AsNoTracking()
            .Where(p => p.Id == propertyId)
            .Where(Property.PubliclyViewable(utcNow))
            .Select(p => new ReportableListing(p.Id, p.Title, p.OwnerId))
            .FirstOrDefaultAsync(ct);

    public async Task<ListingSnapshot?> GetListingSnapshotAsync(Guid propertyId, DateTime utcNow, CancellationToken ct = default)
    {
        var p = await _db.Properties.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == propertyId, ct);
        if (p is null)
            return null;

        var visible = !p.IsDeleted && Property.PubliclyViewable(utcNow).Compile()(p);
        var price = p.ListingType == ListingType.ForSale ? p.PurchasePrice : p.ColdRent ?? p.WarmRent ?? p.PurchasePrice;

        return new ListingSnapshot(
            p.Id, p.Title, p.OwnerId, p.ListingType, p.Status.ToString(), p.IsPublished, p.IsTakenDown,
            p.TakenDownReason, p.IsDeleted, visible, price, p.CurrencyCode, p.City, p.PublishedAt, p.ExpiresAt);
    }

    public async Task<(int Total, int Dismissed)> GetReporterHistoryAsync(Guid reporterUserId, CancellationToken ct = default)
    {
        var mine = _db.ListingReports.AsNoTracking().Where(r => r.ReporterUserId == reporterUserId);
        var total = await mine.CountAsync(ct);
        var dismissed = await mine.CountAsync(r => r.Status == ListingReportStatus.Dismissed, ct);
        return (total, dismissed);
    }

    public async Task<IReadOnlyList<UserLabel>> ListAdminsAsync(CancellationToken ct = default)
    {
        var adminIds = await AdminIds().ToListAsync(ct);
        var labels = await GetUserLabelsAsync(adminIds, ct);
        return labels.Select(kv => new UserLabel(kv.Key, kv.Value)).OrderBy(l => l.Name, StringComparer.CurrentCulture).ToList();
    }

    public Task<bool> IsActiveAdminAsync(Guid userId, CancellationToken ct = default)
        => AdminIds().AnyAsync(id => id == userId, ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetUserLabelsAsync(IEnumerable<Guid> userIds, CancellationToken ct = default)
    {
        var ids = userIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<Guid, string>();

        // Names are decrypted by EF value converters, so they are read whole and composed here.
        var accounts = await _db.UserAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .Select(a => new { a.Id, a.DisplayName, a.FirstName, a.LastName })
            .ToListAsync(ct);

        return accounts.ToDictionary(
            a => a.Id,
            a => !string.IsNullOrWhiteSpace(a.DisplayName)
                ? a.DisplayName!
                : $"{a.FirstName} {a.LastName}".Trim() is { Length: > 0 } full ? full : "-");
    }

    // ── helpers ──

    // A class with member initialisers (not a positional record): EF cannot order by a member of an
    // intermediate constructor projection.
    private sealed class ReportWithListing
    {
        public ListingReport Report { get; set; } = null!;
        public string? Title { get; set; }
        public ListingType? ListingType { get; set; }
        public bool? Deleted { get; set; }
        public bool? TakenDown { get; set; }
    }

    private IQueryable<ReportWithListing> Joined() =>
        from r in _db.ListingReports.AsNoTracking()
        join p in _db.Properties.IgnoreQueryFilters().AsNoTracking() on r.PropertyId equals p.Id into listings
        from p in listings.DefaultIfEmpty()
        select new ReportWithListing
        {
            Report = r,
            Title = p == null ? null : p.Title,
            ListingType = p == null ? null : (ListingType?)p.ListingType,
            Deleted = p == null ? null : (bool?)p.IsDeleted,
            TakenDown = p == null ? null : (bool?)(p.TakenDownAt != null),
        };

    private static IQueryable<ReportWithListing> ApplyFilter(
        IQueryable<ReportWithListing> query,
        ListingReportFilter filter,
        IReadOnlyDictionary<ListingReportPriority, DateTime> overdueCutoffs,
        bool includeStatusFilters)
    {
        if (includeStatusFilters)
        {
            if (filter.Status is { } status)
                query = query.Where(x => x.Report.Status == status);
            else if (filter.OpenOnly)
                query = query.Where(x => OpenStatuses.Contains(x.Report.Status));

            if (filter.OverdueOnly)
            {
                var overdue = OverduePredicate(overdueCutoffs);
                query = query.Where(Compose(overdue));
            }
        }

        if (filter.Reason is { } reason)
            query = query.Where(x => x.Report.Reason == reason);

        if (filter.Priority is { } priority)
            query = query.Where(x => x.Report.Priority == priority);

        if (filter.UnassignedOnly)
            query = query.Where(x => x.Report.AssignedAdminId == null);
        else if (filter.AssignedAdminId is { } assignee)
            query = query.Where(x => x.Report.AssignedAdminId == assignee);

        if (filter.PropertyId is { } propertyId)
            query = query.Where(x => x.Report.PropertyId == propertyId);

        if (filter.FromUtc is { } from)
            query = query.Where(x => x.Report.CreatedAt >= from);

        if (filter.ToUtc is { } to)
            query = query.Where(x => x.Report.CreatedAt < to);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            var number = TryParseReference(term);
            var id = Guid.TryParse(term, out var g) ? g : (Guid?)null;
            var pattern = $"%{EscapeLike(term)}%";
            query = query.Where(x =>
                (number != null && x.Report.Number == number) ||
                (id != null && (x.Report.PropertyId == id || x.Report.Id == id)) ||
                EF.Functions.ILike(x.Report.ListingTitleSnapshot, pattern, "\\") ||
                (x.Title != null && EF.Functions.ILike(x.Title, pattern, "\\")));
        }

        return query;
    }

    /// <summary>Open, and created before its priority's review cutoff.</summary>
    private static Expression<Func<ListingReport, bool>> OverduePredicate(IReadOnlyDictionary<ListingReportPriority, DateTime> cutoffs)
    {
        var urgent = cutoffs[ListingReportPriority.Urgent];
        var high = cutoffs[ListingReportPriority.High];
        var normal = cutoffs[ListingReportPriority.Normal];
        var low = cutoffs[ListingReportPriority.Low];

        return r => OpenStatuses.Contains(r.Status) && (
            (r.Priority == ListingReportPriority.Urgent && r.CreatedAt < urgent) ||
            (r.Priority == ListingReportPriority.High && r.CreatedAt < high) ||
            (r.Priority == ListingReportPriority.Normal && r.CreatedAt < normal) ||
            (r.Priority == ListingReportPriority.Low && r.CreatedAt < low));
    }

    private static Expression<Func<ReportWithListing, bool>> Compose(Expression<Func<ListingReport, bool>> inner)
    {
        var x = Expression.Parameter(typeof(ReportWithListing), "x");
        var body = new ReplaceParameter(inner.Parameters[0], Expression.Property(x, nameof(ReportWithListing.Report))).Visit(inner.Body);
        return Expression.Lambda<Func<ReportWithListing, bool>>(body!, x);
    }

    private sealed class ReplaceParameter(ParameterExpression from, Expression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }

    private IQueryable<Guid> AdminIds() =>
        from user in _db.Users.AsNoTracking()
        join userRole in _db.UserRoles.AsNoTracking() on user.Id equals userRole.UserId
        join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
        join account in _db.UserAccounts.AsNoTracking() on user.Id equals account.Id
        where role.Name == RoleNames.Admin && !account.IsDeleted
        select user.Id;

    /// <summary>"RPT-000042", "rpt-42" or "42" → 42.</summary>
    private static long? TryParseReference(string term)
    {
        var digits = term.StartsWith("RPT-", StringComparison.OrdinalIgnoreCase) ? term[4..] : term;
        return long.TryParse(digits, out var n) && n > 0 ? n : null;
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
