using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Services.Interfaces;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Services;

public sealed class GovernorateLookup : IGovernorateLookup
{
    private readonly AppDbContext _db;
    public GovernorateLookup(AppDbContext db) => _db = db;

    public async Task<IReadOnlyList<int>> GetKnownIdsAsync(IEnumerable<int> ids, CancellationToken ct = default)
    {
        var wanted = ids.Distinct().ToArray();
        if (wanted.Length == 0)
            return [];

        return await _db.Governorates
            .AsNoTracking()
            .Where(g => g.IsActive && wanted.Contains(g.Id))
            .Select(g => g.Id)
            .ToListAsync(ct);
    }
}
