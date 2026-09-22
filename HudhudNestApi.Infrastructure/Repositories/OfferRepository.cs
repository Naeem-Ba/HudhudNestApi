using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Marketing.Interfaces;
using HudhudNestApi.Domain.Marketing.Entities;
using HudhudNestApi.Domain.Marketing.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories;

public sealed class OfferRepository : IOfferRepository
{
    private readonly AppDbContext _db;

    public OfferRepository(AppDbContext db)
        => _db = db;

    public void Add(Offer offer)
    {
        _db.Offers.Add(offer);
    }

    public Task<Offer?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return _db.Offers.FindAsync([id], ct).AsTask();
    }

    public Task<Offer?> GetCurrentActiveAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        return _db.Offers
            .AsNoTracking()
            .Where(o => o.Status == OfferStatus.Active)
            .Where(o => o.StartsAtUtc <= nowUtc)
            .Where(o => o.EndsAtUtc == null || o.EndsAtUtc >= nowUtc)
            // If more than one is somehow ever active at once, prefer the one closer to
            // running out — the more urgent/specific offer to surface.
            .OrderBy(o => o.EndsAtUtc == null)
            .ThenBy(o => o.EndsAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Offer>> GetAllAsync(CancellationToken ct = default)
    {
        return await _db.Offers.AsNoTracking().ToListAsync(ct);
    }

    public async Task<bool> TryReserveRedemptionAsync(Guid offerId, CancellationToken ct = default)
    {
        var nowUtc = DateTime.UtcNow;

        // Single atomic UPDATE — see Offer's class doc comment. Deliberately re-checks
        // Status/date/cap at the database row level rather than trusting a previously
        // loaded snapshot, so two concurrent requests racing for the last slot cannot both
        // succeed: only the first UPDATE's WHERE clause still matches.
        var affected = await _db.Offers
            .Where(o => o.Id == offerId)
            .Where(o => o.Status == OfferStatus.Active)
            .Where(o => o.StartsAtUtc <= nowUtc)
            .Where(o => o.EndsAtUtc == null || o.EndsAtUtc >= nowUtc)
            .Where(o => o.MaxRedemptions == null || o.RedeemedCount < o.MaxRedemptions)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(o => o.RedeemedCount, o => o.RedeemedCount + 1),
                ct);

        return affected == 1;
    }
}
