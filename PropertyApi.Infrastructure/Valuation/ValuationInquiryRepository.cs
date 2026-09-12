using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Valuation;

public sealed class ValuationInquiryRepository : IValuationInquiryRepository
{
    private readonly AppDbContext _db;

    public ValuationInquiryRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<ValuationInquiry?> GetByIdAsync(Guid id, CancellationToken ct = default)
        // Tracked on purpose: every caller that loads one here (the SLA sweep included) goes
        // on to mutate it through Expire()/MarkX and expects SaveChangesAsync to persist that —
        // same reasoning AgencyRepository.GetByIdAsync documents for itself.
        => await _db.ValuationInquiries.FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task AddAsync(ValuationInquiry inquiry, CancellationToken ct = default)
        => await _db.ValuationInquiries.AddAsync(inquiry, ct);

    public void Update(ValuationInquiry inquiry)
        => _db.ValuationInquiries.Update(inquiry);

    public async Task<IReadOnlyList<ValuationInquiry>> GetDueForExpiryAsync(
        DateTime utcNow,
        int batchSize,
        CancellationToken ct = default)
        => await ApplyDueForExpiryFilter(_db.ValuationInquiries, utcNow)
            .OrderBy(i => i.ExpiresAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ValuationInquiry>> GetDueForReminderAsync(
        DateTime utcNow,
        int batchSize,
        CancellationToken ct = default)
        => await ApplyDueForReminderFilter(_db.ValuationInquiries, utcNow)
            .OrderBy(i => i.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ValuationInquiry>> GetExpiredAwaitingNotificationAsync(
        int batchSize,
        CancellationToken ct = default)
        => await _db.ValuationInquiries
            .Where(i =>
                i.Status == ValuationInquiryStatus.Expired
                && i.ExpiryNotifiedAt == null
                && i.RequesterId != null)
            .OrderBy(i => i.ExpiresAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ValuationInquiry>> GetCompletedAwaitingResultNotificationAsync(
        int batchSize,
        CancellationToken ct = default)
        => await _db.ValuationInquiries
            .Where(i =>
                i.Status == ValuationInquiryStatus.Completed
                && i.ResultReadyNotifiedAt == null
                && i.RequesterId != null)
            .OrderBy(i => i.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<PagedResult<ValuationInquiry>> GetPagedAsync(
        ValuationInquiryStatus? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _db.ValuationInquiries.AsNoTracking().AsQueryable();

        if (status is { } s)
        {
            query = query.Where(i => i.Status == s);
        }

        // Database-side count + page, not "ToListAsync then .Count/.Skip/.Take" — the whole
        // point of this method existing (see its own doc comment on the interface).
        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<ValuationInquiry>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// The actual "never left indefinitely pending" rule behind
    /// <see cref="GetDueForExpiryAsync"/>, pulled out as a public static IQueryable transform —
    /// same pattern as AgencyRepository.ApplyActiveLocationFilter — so
    /// PropertyApi.Architecture.Tests can exercise it over an in-memory
    /// List&lt;ValuationInquiry&gt;.AsQueryable() with no database involved. Deliberately NOT
    /// tracked/filtered here beyond IsDeleted — this method takes tracked or untracked queries
    /// alike, per caller.
    /// </summary>
    public static IQueryable<ValuationInquiry> ApplyDueForExpiryFilter(
        IQueryable<ValuationInquiry> query,
        DateTime utcNow)
        => query.Where(i =>
            (i.Status == ValuationInquiryStatus.Pending
                || i.Status == ValuationInquiryStatus.MatchedFromListings
                || i.Status == ValuationInquiryStatus.AwaitingOfficeResponses)
            && i.ExpiresAt <= utcNow);

    /// <summary>
    /// Remediation M4 — the "closing soon" reminder rule behind
    /// <see cref="GetDueForReminderAsync"/>, pulled out the same way ApplyDueForExpiryFilter
    /// is, for the same in-memory-testable reason. Never overlaps with
    /// ApplyDueForExpiryFilter's own non-terminal statuses AND ExpiresAt in the past — an
    /// inquiry that has already crossed its 24h ExpiresAt is handled by the expiry phase, not
    /// this one, even if a reminder was never sent for it (a "closing soon" reminder after the
    /// deadline already passed is not a reminder, it is a stale message).
    /// </summary>
    /// <remarks>
    /// The 18h cutoff is expressed as <c>i.CreatedAt &lt;= reminderCutoff</c> (with
    /// <c>reminderCutoff = utcNow - ReminderWindow</c> computed once, outside the expression
    /// tree) rather than <c>i.CreatedAt.Add(ReminderWindow) &lt;= utcNow</c>: the Npgsql EF Core
    /// provider only translates the named <c>DateTime.AddXxx</c> overloads (AddDays, AddHours,
    /// …), not the generic <c>Add(TimeSpan)</c> overload, so the latter fails at query time with
    /// "The LINQ expression … could not be translated." Subtracting a constant TimeSpan from
    /// utcNow first is exact (DateTime/TimeSpan arithmetic is tick-based, no rounding) and keeps
    /// the comparison itself translatable, since both provider-side dispositions of `x + c &lt;=
    /// y` and `x &lt;= y - c` are the same inequality.
    /// </remarks>
    public static IQueryable<ValuationInquiry> ApplyDueForReminderFilter(
        IQueryable<ValuationInquiry> query,
        DateTime utcNow)
    {
        var reminderCutoff = utcNow - ValuationInquiry.ReminderWindow;
        return query.Where(i =>
            (i.Status == ValuationInquiryStatus.Pending
                || i.Status == ValuationInquiryStatus.MatchedFromListings
                || i.Status == ValuationInquiryStatus.AwaitingOfficeResponses)
            && i.ReminderSentAt == null
            && i.RequesterId != null
            && i.CreatedAt <= reminderCutoff
            && i.ExpiresAt > utcNow);
    }
}
