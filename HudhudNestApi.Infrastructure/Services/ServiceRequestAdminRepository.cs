using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Services.Admin;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Domain.Services.Enums;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Application.Common.Paging;

namespace HudhudNestApi.Infrastructure.Services;

public sealed class ServiceRequestAdminRepository : IServiceRequestAdminRepository
{
    private readonly AppDbContext _db;
    public ServiceRequestAdminRepository(AppDbContext db) => _db = db;

    public async Task<(IReadOnlyList<AdminServiceRequestSummaryDto> Items, int Total)> SearchAsync(
        AdminServiceRequestFilter filter, DateTime utcNow, CancellationToken ct = default)
    {
        var staleBefore = utcNow - IServiceRequestAdminRepository.StaleAfter;

        var query = _db.ServiceRequests.AsNoTracking().AsQueryable();

        if (filter.Status is { } status) query = query.Where(r => r.Status == status);
        if (filter.Category is { } category) query = query.Where(r => r.Category == category);
        if (filter.OfferingId is { } offeringId) query = query.Where(r => r.ServiceOfferingId == offeringId);
        if (filter.ProviderId is { } providerId) query = query.Where(r => r.ServiceProviderId == providerId);
        if (filter.From is { } from) query = query.Where(r => r.CreatedAt >= from);
        if (filter.To is { } to) query = query.Where(r => r.CreatedAt <= to);
        if (filter.Confirmed is { } confirmed)
            query = confirmed ? query.Where(r => r.ConfirmedAt != null) : query.Where(r => r.ConfirmedAt == null);

        if (filter.NeedsAttentionOnly)
        {
            query = query.Where(r =>
                r.RequesterAnswer == ServiceRequesterAnswer.Disputed
                || ((r.Status == ServiceRequestStatus.Inquiry
                        || r.Status == ServiceRequestStatus.UnderReview
                        || r.Status == ServiceRequestStatus.Accepted
                        || r.Status == ServiceRequestStatus.Scheduled
                        || r.Status == ServiceRequestStatus.InProgress)
                    && r.LastActivityAt < staleBefore));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var like = "%" + EscapeLike(filter.Search!) + "%";
            query = query.Where(r =>
                EF.Functions.ILike(r.RequestNumber, like, "\\")
                || EF.Functions.ILike(r.Property!.Title, like, "\\")
                || EF.Functions.ILike(r.ServiceProvider!.DisplayName, like, "\\")
                || EF.Functions.ILike(r.ServiceOffering!.Title, like, "\\"));
        }

        var total = await query.CountAsync(ct);

        var rows = await query
            .OrderByDescending(r => r.CreatedAt)
            .SkipPage(filter.Page, filter.PageSize)
            .Take(filter.PageSize)
            .Select(r => new
            {
                r.Id,
                r.RequestNumber,
                r.PropertyId,
                PropertyTitle = r.Property!.Title,
                RequesterDisplayName = r.Requester!.DisplayName,
                RequesterFirstName = r.Requester!.FirstName,
                RequesterLastName = r.Requester!.LastName,
                r.ServiceProviderId,
                ProviderName = r.ServiceProvider!.DisplayName,
                r.ServiceOfferingId,
                OfferingTitle = r.ServiceOffering!.Title,
                r.Category,
                r.Status,
                r.OutcomeReason,
                r.RequesterAnswer,
                r.RequesterReason,
                Rating = _db.ServiceReviews
                    .Where(v => v.ServiceRequestId == r.Id)
                    .Select(v => (int?)v.Rating)
                    .FirstOrDefault(),
                r.CreatedAt,
                r.UpdatedAt,
                r.Intent,
                r.ConfirmedAt,
                r.LastActivityAt,
                r.ExpiredWaitingOn,
                MessageCount = _db.ServiceRequestMessages.Count(m => m.ServiceRequestId == r.Id),
            })
            .ToListAsync(ct);

        var items = rows.Select(r => new AdminServiceRequestSummaryDto(
            r.Id,
            r.RequestNumber,
            r.PropertyId,
            r.PropertyTitle,
            string.IsNullOrWhiteSpace(r.RequesterDisplayName)
                ? $"{r.RequesterFirstName} {r.RequesterLastName}".Trim()
                : r.RequesterDisplayName!,
            r.ServiceProviderId,
            r.ProviderName,
            r.ServiceOfferingId,
            r.OfferingTitle,
            r.Category,
            r.Status,
            r.OutcomeReason,
            r.RequesterAnswer,
            r.RequesterReason,
            r.Rating,
            r.RequesterAnswer == ServiceRequesterAnswer.Disputed
                || (IsOpen(r.Status) && r.LastActivityAt < staleBefore),
            r.CreatedAt,
            r.UpdatedAt,
            r.Intent,
            r.ConfirmedAt,
            r.LastActivityAt,
            r.ExpiredWaitingOn,
            r.MessageCount)).ToList();

        return (items, total);
    }

    public async Task<ServiceStatsDto> GetStatsAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
    {
        var requests = _db.ServiceRequests.AsNoTracking().AsQueryable();
        if (from is { } f) requests = requests.Where(r => r.CreatedAt >= f);
        if (to is { } t) requests = requests.Where(r => r.CreatedAt <= t);

        // One grouped query per offering: counts per status and the disputed count. The
        // category and provider tables below are sums of these rows, so all three tables
        // always agree with each other.
        var counts = await requests
            .GroupBy(r => new { r.ServiceOfferingId, r.ServiceProviderId, r.Category })
            .Select(g => new
            {
                g.Key.ServiceOfferingId,
                g.Key.ServiceProviderId,
                g.Key.Category,
                Inquiries = g.Count(),
                OpenInquiries = g.Count(r => r.Status == ServiceRequestStatus.Inquiry),
                // Conversations that never became a request: dropped by either side or by the clock.
                InquiriesDropped = g.Count(r => r.ConfirmedAt == null
                    && (r.Status == ServiceRequestStatus.Rejected
                        || r.Status == ServiceRequestStatus.Cancelled
                        || r.Status == ServiceRequestStatus.Expired)),
                Requested = g.Count(r => r.ConfirmedAt != null),
                Open = g.Count(r => r.Status == ServiceRequestStatus.UnderReview
                    || r.Status == ServiceRequestStatus.Accepted
                    || r.Status == ServiceRequestStatus.Scheduled
                    || r.Status == ServiceRequestStatus.InProgress),
                Completed = g.Count(r => r.Status == ServiceRequestStatus.Completed
                    || r.Status == ServiceRequestStatus.Reviewed),
                NotCompleted = g.Count(r => r.Status == ServiceRequestStatus.NotCompleted),
                // Confirmed requests only: a withdrawn inquiry is not a cancelled request.
                Rejected = g.Count(r => r.ConfirmedAt != null && r.Status == ServiceRequestStatus.Rejected),
                Cancelled = g.Count(r => r.ConfirmedAt != null && r.Status == ServiceRequestStatus.Cancelled),
                Expired = g.Count(r => r.ConfirmedAt != null && r.Status == ServiceRequestStatus.Expired),
                Disputed = g.Count(r => r.RequesterAnswer == ServiceRequesterAnswer.Disputed),
            })
            .ToListAsync(ct);

        var ratings = await (
            from review in _db.ServiceReviews.AsNoTracking()
            join request in requests on review.ServiceRequestId equals request.Id
            where !review.IsHidden
            group review by request.ServiceOfferingId into g
            select new { OfferingId = g.Key, Sum = g.Sum(x => x.Rating), Count = g.Count() })
            .ToListAsync(ct);
        var ratingByOffering = ratings.ToDictionary(x => x.OfferingId, x => (x.Sum, x.Count));

        // Every offering is listed, also those nobody requested: "never asked for" is the
        // answer to "which services are actually wanted".
        var offerings = await _db.ServiceOfferings.AsNoTracking().IgnoreQueryFilters()
            .Select(o => new
            {
                o.Id,
                o.Title,
                o.Category,
                o.IsActive,
                o.ServiceProviderId,
                ProviderName = o.ServiceProvider!.DisplayName,
            })
            .ToListAsync(ct);

        var byOfferingId = counts.ToDictionary(c => c.ServiceOfferingId);
        var providerNames = new Dictionary<Guid, string>();

        var offeringRows = new List<(Guid Id, string Title, string Provider, Guid ProviderId, ServiceCategory Category, bool Active,
            Cell Cell)>();

        foreach (var o in offerings)
        {
            providerNames[o.ServiceProviderId] = o.ProviderName;
            byOfferingId.TryGetValue(o.Id, out var c);
            ratingByOffering.TryGetValue(o.Id, out var r);
            offeringRows.Add((o.Id, o.Title, o.ProviderName, o.ServiceProviderId, o.Category, o.IsActive,
                c is null
                    ? new Cell(0, 0, 0, 0, 0, 0, 0, r.Sum, r.Count, 0, 0, 0, 0)
                    : new Cell(c.Requested, c.Open, c.Completed, c.NotCompleted, c.Rejected, c.Cancelled, c.Disputed, r.Sum, r.Count,
                        c.Inquiries, c.OpenInquiries, c.InquiriesDropped, c.Expired)));
        }

        var byOffering = offeringRows
            .Select(o => ToRow(o.Id.ToString(), o.Id, o.Title, o.Provider, o.Active, o.Cell))
            .OrderByDescending(x => x.Requested).ThenBy(x => x.Label)
            .ToList();

        var byCategory = Enum.GetValues<ServiceCategory>()
            .Select(cat => ToRow(cat.ToString(), null, cat.ToString(), null, null,
                Sum(offeringRows.Where(o => o.Category == cat).Select(o => o.Cell))))
            .ToList();

        var byProvider = offeringRows
            .GroupBy(o => o.ProviderId)
            .Select(g => ToRow(g.Key.ToString(), g.Key, providerNames[g.Key], null, null, Sum(g.Select(o => o.Cell))))
            .OrderByDescending(x => x.Requested).ThenBy(x => x.Label)
            .ToList();

        var totals = ToRow("total", null, null, null, null, Sum(offeringRows.Select(o => o.Cell)));

        var providerReasons = await requests
            .Where(r => r.OutcomeReason != null)
            .GroupBy(r => r.OutcomeReason!.Value)
            .Select(g => new { Reason = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var requesterReasons = await requests
            .Where(r => r.RequesterReason != null)
            .GroupBy(r => r.RequesterReason!.Value)
            .Select(g => new { Reason = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Who stayed silent when a request expired: the provider never answered, or the customer did not come back.
        var expiry = await requests
            .Where(r => r.ExpiredWaitingOn != null)
            .GroupBy(r => r.ExpiredWaitingOn!.Value)
            .Select(g => new { Party = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new ServiceStatsDto(
            from, to, totals, byCategory, byOffering, byProvider,
            providerReasons.OrderByDescending(x => x.Count).Select(x => new ReasonCountDto(x.Reason.ToString(), x.Count)).ToList(),
            requesterReasons.OrderByDescending(x => x.Count).Select(x => new ReasonCountDto(x.Reason.ToString(), x.Count)).ToList(),
            expiry.OrderByDescending(x => x.Count).Select(x => new ReasonCountDto(x.Party.ToString(), x.Count)).ToList());
    }

    private sealed record Cell(
        int Requested, int Open, int Completed, int NotCompleted, int Rejected, int Cancelled, int Disputed,
        int RatingSum, int RatingCount,
        int Inquiries, int OpenInquiries, int InquiriesDropped, int Expired);

    private static Cell Sum(IEnumerable<Cell> cells)
        => cells.Aggregate(new Cell(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), (a, c) => new Cell(
            a.Requested + c.Requested, a.Open + c.Open, a.Completed + c.Completed,
            a.NotCompleted + c.NotCompleted, a.Rejected + c.Rejected, a.Cancelled + c.Cancelled,
            a.Disputed + c.Disputed, a.RatingSum + c.RatingSum, a.RatingCount + c.RatingCount,
            a.Inquiries + c.Inquiries, a.OpenInquiries + c.OpenInquiries,
            a.InquiriesDropped + c.InquiriesDropped, a.Expired + c.Expired));

    private static ServiceStatsRowDto ToRow(string key, Guid? id, string? label, string? subLabel, bool? active, Cell c)
        => new(key, id, label, subLabel, active, c.Requested, c.Open, c.Completed, c.NotCompleted, c.Rejected,
            c.Cancelled, c.Disputed,
            c.RatingCount == 0 ? null : Math.Round((double)c.RatingSum / c.RatingCount, 2),
            c.RatingCount, c.Inquiries, c.OpenInquiries, c.InquiriesDropped, c.Expired);

    private static bool IsOpen(ServiceRequestStatus s)
        => s is ServiceRequestStatus.Inquiry or ServiceRequestStatus.UnderReview or ServiceRequestStatus.Accepted
            or ServiceRequestStatus.Scheduled or ServiceRequestStatus.InProgress;

    private static string EscapeLike(string s)
        => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
