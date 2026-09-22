using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Valuation.DTOs;
using HudhudNestApi.Application.Valuation.Interfaces;
using HudhudNestApi.Domain.Valuation.Entities;
using HudhudNestApi.Domain.Valuation.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Valuation;

public sealed class ValuationOfficeInvitationRepository : IValuationOfficeInvitationRepository
{
    private readonly AppDbContext _db;

    public ValuationOfficeInvitationRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddRangeAsync(
        IEnumerable<ValuationOfficeInvitation> invitations,
        CancellationToken ct = default)
        => await _db.ValuationOfficeInvitations.AddRangeAsync(invitations, ct);

    public void Update(ValuationOfficeInvitation invitation)
        => _db.ValuationOfficeInvitations.Update(invitation);

    public async Task<ValuationOfficeInvitation?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.ValuationOfficeInvitations.FirstOrDefaultAsync(i => i.Id == id, ct);

    public async Task<IReadOnlyList<ValuationOfficeInvitation>> GetByInquiryIdAsync(
        Guid inquiryId,
        CancellationToken ct = default)
        => await _db.ValuationOfficeInvitations
            .Where(i => i.InquiryId == inquiryId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ValuationOfficeInvitation>> GetByAgencyIdAsync(
        Guid agencyId,
        CancellationToken ct = default)
        // AsNoTracking: Stage 6's dashboard is read-only — nothing here goes on to mutate and
        // save these rows (unlike GetByIdAsync/GetStaleSentInvitationsAsync above).
        => await _db.ValuationOfficeInvitations
            .AsNoTracking()
            .Where(i => i.AgencyId == agencyId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ValuationOfficeInvitation>> GetStaleSentInvitationsAsync(
        DateTime utcNow,
        int batchSize,
        CancellationToken ct = default)
        // Tracked on purpose (same reasoning as ValuationInquiryRepository.GetByIdAsync): the
        // SLA sweep goes straight on to call Expire() on every row this returns.
        => await ApplyStaleSentFilter(_db.ValuationOfficeInvitations, _db.ValuationInquiries, utcNow)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ValuationOfficeInvitation>> GetExpiredAwaitingNotificationAsync(
        int batchSize,
        CancellationToken ct = default)
        => await _db.ValuationOfficeInvitations
            .Where(i => i.Status == ValuationOfficeInvitationStatus.Expired && i.ExpiryNotifiedAt == null)
            .OrderBy(i => i.SentAt)
            .Take(batchSize)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ValuationOfficeInvitationCountsRow>> GetInvitationCountsByAgencyAsync(
        CancellationToken ct = default)
        // GROUP BY at the database — the result set is one row per agency (small), not one
        // row per invitation (potentially large), so this scales independently of how many
        // invitations have ever been sent.
        => await _db.ValuationOfficeInvitations
            .AsNoTracking()
            .GroupBy(i => i.AgencyId)
            .Select(g => new ValuationOfficeInvitationCountsRow(
                g.Key,
                g.Count(),
                g.Count(i => i.Status == ValuationOfficeInvitationStatus.Responded)))
            .ToListAsync(ct);

    /// <summary>
    /// The actual "no invitation left Sent past its usefulness" rule behind
    /// <see cref="GetStaleSentInvitationsAsync"/>, pulled out as a public static IQueryable
    /// transform over two queryables (no Domain-level navigation property exists between the
    /// two entities — see IValuationOfficeInvitationRepository's doc comment) — same
    /// public-static-transform pattern as AgencyRepository.ApplyActiveLocationFilter /
    /// ValuationInquiryRepository.ApplyDueForExpiryFilter, so
    /// HudhudNestApi.Architecture.Tests can exercise the real join over in-memory
    /// List&lt;T&gt;.AsQueryable() pairs with no database involved.
    /// </summary>
    public static IQueryable<ValuationOfficeInvitation> ApplyStaleSentFilter(
        IQueryable<ValuationOfficeInvitation> invitations,
        IQueryable<ValuationInquiry> inquiries,
        DateTime utcNow)
        => invitations
            .Where(i => i.Status == ValuationOfficeInvitationStatus.Sent)
            .Join(
                inquiries,
                invitation => invitation.InquiryId,
                inquiry => inquiry.Id,
                (invitation, inquiry) => new { Invitation = invitation, Inquiry = inquiry })
            .Where(x =>
                x.Inquiry.Status == ValuationInquiryStatus.Completed
                || x.Inquiry.Status == ValuationInquiryStatus.Expired
                || x.Inquiry.ExpiresAt <= utcNow)
            .OrderBy(x => x.Inquiry.ExpiresAt)
            .Select(x => x.Invitation);
}
