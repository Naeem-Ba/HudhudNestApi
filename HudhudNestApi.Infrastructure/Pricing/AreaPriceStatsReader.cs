using HudhudNestApi.Application.Listings.Currencies;
using HudhudNestApi.Application.Pricing;
using HudhudNestApi.Domain.Pricing;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HudhudNestApi.Infrastructure.Pricing;

public sealed class AreaPriceStatsReader : IAreaPriceStatsReader
{
    private readonly AppDbContext _db;

    public AreaPriceStatsReader(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<AreaPriceStat>> GetForGovernorateAsync(int governorateId, PriceKind kind, CancellationToken ct) =>
        await _db.AreaPriceStats.AsNoTracking()
            .Where(s => s.GovernorateId == governorateId && s.Kind == kind)
            .ToListAsync(ct);

    public Task<CurrencyRates> LoadRatesAsync(CancellationToken ct) => PropertyRepository.LoadCurrencyRatesAsync(_db, ct);
}
