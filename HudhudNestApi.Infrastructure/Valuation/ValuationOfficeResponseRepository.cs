using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Valuation.Interfaces;
using HudhudNestApi.Domain.Valuation.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Valuation;

public sealed class ValuationOfficeResponseRepository : IValuationOfficeResponseRepository
{
    private readonly AppDbContext _db;

    public ValuationOfficeResponseRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(ValuationOfficeResponse response, CancellationToken ct = default)
        => await _db.ValuationOfficeResponses.AddAsync(response, ct);

    public async Task<ValuationOfficeResponse?> GetByInvitationIdAsync(
        Guid invitationId,
        CancellationToken ct = default)
        // AsNoTracking: read-only lookup (dashboard enrichment) — never mutated by this call.
        => await _db.ValuationOfficeResponses
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.InvitationId == invitationId, ct);

    public async Task<IReadOnlyDictionary<Guid, int>> GetWithinSlaResponseCountsByAgencyAsync(
        CancellationToken ct = default)
    {
        // No Domain-level navigation exists between Response → Invitation → Inquiry (same
        // reasoning IValuationOfficeInvitationRepository.GetStaleSentInvitationsAsync's own
        // doc comment gives), so the join lives here, entirely database-side, grouped by
        // agency — never "load every response into memory and match up in C#".
        var rows = await _db.ValuationOfficeResponses
            .AsNoTracking()
            .Join(
                _db.ValuationOfficeInvitations.AsNoTracking(),
                response => response.InvitationId,
                invitation => invitation.Id,
                (response, invitation) => new { response, invitation })
            .Join(
                _db.ValuationInquiries.AsNoTracking(),
                x => x.invitation.InquiryId,
                inquiry => inquiry.Id,
                (x, inquiry) => new { x.response, x.invitation, inquiry })
            .Where(x => x.response.SubmittedAt <= x.inquiry.ExpiresAt)
            .GroupBy(x => x.invitation.AgencyId)
            .Select(g => new { AgencyId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.AgencyId, r => r.Count);
    }
}
