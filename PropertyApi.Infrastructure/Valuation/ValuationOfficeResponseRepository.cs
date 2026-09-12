using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Domain.Valuation.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Valuation;

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
}
