using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Valuation.Interfaces;
using HudhudNestApi.Domain.Valuation.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Valuation;

public sealed class ValuationContactConsentRepository : IValuationContactConsentRepository
{
    private readonly AppDbContext _db;

    public ValuationContactConsentRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task AddAsync(ValuationContactConsent consent, CancellationToken ct = default)
        => await _db.ValuationContactConsents.AddAsync(consent, ct);

    public async Task<ValuationContactConsent?> GetByInvitationIdAsync(
        Guid invitationId,
        CancellationToken ct = default)
        // AsNoTracking: every caller here is a read-only lookup (idempotency check or
        // dashboard enrichment) — never mutated and saved back.
        => await _db.ValuationContactConsents
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.InvitationId == invitationId, ct);

    public async Task<IReadOnlyList<ValuationContactConsent>> GetByAgencyIdAsync(
        Guid agencyId,
        CancellationToken ct = default)
        => await _db.ValuationContactConsents
            .AsNoTracking()
            .Where(c => c.AgencyId == agencyId)
            .ToListAsync(ct);
}
