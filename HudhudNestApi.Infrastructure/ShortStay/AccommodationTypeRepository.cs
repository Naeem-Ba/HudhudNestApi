using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.ShortStay.Interfaces;
using HudhudNestApi.Domain.ShortStay.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.ShortStay;

public sealed class AccommodationTypeRepository : IAccommodationTypeRepository
{
    private readonly AppDbContext _db;
    public AccommodationTypeRepository(AppDbContext db) => _db = db;

    public async Task<AccommodationType?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _db.AccommodationTypes.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<IReadOnlyList<AccommodationType>> GetActiveAsync(CancellationToken ct = default)
        => await _db.AccommodationTypes
            .Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(ct);
}
