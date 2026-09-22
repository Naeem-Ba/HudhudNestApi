using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Search.Interfaces;
using HudhudNestApi.Domain.Search.Entities;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Repositories;

public sealed class SavedSearchRepository : ISavedSearchRepository
{
    private readonly AppDbContext _db;

    public SavedSearchRepository(AppDbContext db)
    {
        _db = db;
    }

    public void Add(SavedSearch savedSearch)
    {
        _db.SavedSearches.Add(savedSearch);
    }

    public void Remove(SavedSearch savedSearch)
    {
        _db.SavedSearches.Remove(savedSearch);
    }

    public Task<SavedSearch?> GetAsync(Guid id, CancellationToken ct = default)
    {
        return _db.SavedSearches.FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<IReadOnlyList<SavedSearch>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        return await _db.SavedSearches
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SavedSearch>> GetAllAsync(CancellationToken ct = default)
    {
        // Intentionally NOT AsNoTracking: the only caller (SavedSearchMatchHostedService)
        // calls MarkMatched() on the results and expects SaveChangesAsync to persist it.
        return await _db.SavedSearches.ToListAsync(ct);
    }
}
