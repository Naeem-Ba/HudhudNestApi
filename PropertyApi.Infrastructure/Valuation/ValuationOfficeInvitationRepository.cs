using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Domain.Valuation.Enums;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Valuation;

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

    public async Task<IReadOnlyList<ValuationOfficeInvitation>> GetByInquiryIdAsync(
        Guid inquiryId,
        CancellationToken ct = default)
        => await _db.ValuationOfficeInvitations
            .Where(i => i.InquiryId == inquiryId)
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

    /// <summary>
    /// The actual "no invitation left Sent past its usefulness" rule behind
    /// <see cref="GetStaleSentInvitationsAsync"/>, pulled out as a public static IQueryable
    /// transform over two queryables (no Domain-level navigation property exists between the
    /// two entities — see IValuationOfficeInvitationRepository's doc comment) — same
    /// public-static-transform pattern as AgencyRepository.ApplyActiveLocationFilter /
    /// ValuationInquiryRepository.ApplyDueForExpiryFilter, so
    /// PropertyApi.Architecture.Tests can exercise the real join over in-memory
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
