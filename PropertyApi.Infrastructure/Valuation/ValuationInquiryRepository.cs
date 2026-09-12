using Microsoft.EntityFrameworkCore;
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
}
