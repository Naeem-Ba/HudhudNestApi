using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Currencies.Interfaces;
using HudhudNestApi.Domain.Lookups.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories;

public sealed class CurrencyRepository : ICurrencyRepository
{
    private readonly AppDbContext _db;

    public CurrencyRepository(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<Currency>> GetActiveAsync(CancellationToken ct = default)
        => await _db.Currencies.AsNoTracking().Where(c => c.IsActive).ToListAsync(ct);

    public Task<Currency?> GetByCodeAsync(string code, CancellationToken ct = default)
        => _db.Currencies.FirstOrDefaultAsync(c => c.Code == code, ct);

    public void Add(Currency currency) => _db.Currencies.Add(currency);
}
