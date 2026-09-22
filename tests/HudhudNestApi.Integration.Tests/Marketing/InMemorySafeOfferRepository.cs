using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Marketing.Interfaces;
using HudhudNestApi.Domain.Marketing.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Repositories;

namespace HudhudNestApi.Integration.Tests.Marketing;

/// <summary>
/// Test-only stand-in for the real <see cref="OfferRepository"/>'s
/// <c>TryReserveRedemptionAsync</c>: EF Core's InMemory provider (used by
/// <c>TestApplication</c>) does not support <c>ExecuteUpdateAsync</c>, the mechanism the
/// real repository relies on for a single atomic, race-safe UPDATE (see OfferRepository's
/// class-level doc comment) — the exact reason this codebase already swaps
/// <c>IRefreshTokenRepository</c> for an InMemory-safe double in <c>TestApplication</c>
/// (<c>EfInMemoryRefreshTokenRepository</c>).
///
/// This reproduces the same check-then-increment business behavior via a normal tracked
/// update, which is correctness-equivalent under this test host's single-threaded,
/// non-concurrent request execution — it does NOT re-prove the real atomic-UPDATE
/// race-safety guarantee under concurrent load. That guarantee is a Postgres-specific
/// property of <c>ExecuteUpdateAsync</c> translating to one indivisible SQL statement, and
/// belongs to the production code path itself; verifying it under genuine concurrency needs
/// a real-Postgres-backed test host (see <c>InvestmentApiTestFactory</c> for the pattern
/// this repo already uses elsewhere), which requires local Postgres credentials this
/// environment does not have.
/// </summary>
public sealed class InMemorySafeOfferRepository : IOfferRepository
{
    private readonly AppDbContext _db;
    private readonly OfferRepository _inner;

    public InMemorySafeOfferRepository(AppDbContext db)
    {
        _db = db;
        _inner = new OfferRepository(db);
    }

    public void Add(Offer offer) => _inner.Add(offer);

    public Task<Offer?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _inner.GetByIdAsync(id, ct);

    public Task<Offer?> GetCurrentActiveAsync(DateTime nowUtc, CancellationToken ct = default) =>
        _inner.GetCurrentActiveAsync(nowUtc, ct);

    public Task<IReadOnlyList<Offer>> GetAllAsync(CancellationToken ct = default) =>
        _inner.GetAllAsync(ct);

    public async Task<bool> TryReserveRedemptionAsync(Guid offerId, CancellationToken ct = default)
    {
        var offer = await _db.Offers.FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null || !offer.IsCurrentlyRedeemable(DateTime.UtcNow))
            return false;

        // RedeemedCount has a private setter (see Offer's class doc comment on why it must
        // only ever change through this exact check-then-increment path) — EF's
        // change-tracker API can still write it directly without going through the CLR
        // property setter, same as the production ExecuteUpdateAsync path never calls it
        // either.
        _db.Entry(offer).Property(o => o.RedeemedCount).CurrentValue = offer.RedeemedCount + 1;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
