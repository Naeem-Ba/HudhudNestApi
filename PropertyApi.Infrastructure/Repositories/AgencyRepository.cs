using Microsoft.EntityFrameworkCore;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Domain.Agencies.Entities;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Repositories;

public sealed class AgencyRepository : IAgencyRepository
{
    private readonly AppDbContext _db;

    public AgencyRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<Agency?> GetByIdAsync(Guid agencyId, CancellationToken ct = default)
        => await _db.Agencies.FirstOrDefaultAsync(a => a.Id == agencyId, ct);

    public async Task<Agency?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => await _db.Agencies
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Slug == slug, ct);

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default)
        => await _db.Agencies
            .AsNoTracking()
            .AnyAsync(a => a.Slug == slug, ct);

    public async Task<Agency?> GetByOwnerAsync(Guid ownerUserId, CancellationToken ct = default)
        => await _db.Agencies
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.OwnerUserId == ownerUserId, ct);

    public async Task AddAsync(Agency agency, CancellationToken ct = default)
        => await _db.Agencies.AddAsync(agency, ct);

    public void Update(Agency agency)
        => _db.Agencies.Update(agency);

    public async Task<UserAccount?> GetUserAccountAsync(Guid userId, CancellationToken ct = default)
        // Tracked on purpose: every caller that loads an account here goes on to mutate it
        // through JoinAgency/LeaveAgency and expects SaveChangesAsync to persist that.
        => await _db.UserAccounts.FirstOrDefaultAsync(u => u.Id == userId, ct);

    public async Task<IReadOnlyList<UserAccount>> GetMembersAsync(
        Guid agencyId,
        CancellationToken ct = default)
        => await _db.UserAccounts
            .AsNoTracking()
            .Where(u => u.AgencyId == agencyId)
            .OrderBy(u => u.AgencyJoinedAt)
            .ToListAsync(ct);

    public async Task<int> CountMembersAsync(Guid agencyId, CancellationToken ct = default)
        => await _db.UserAccounts
            .AsNoTracking()
            .CountAsync(u => u.AgencyId == agencyId, ct);

    public async Task<int> ClearAgencyAttributionAsync(
        Guid agencyId,
        CancellationToken ct = default)
    {
        // Loaded and mutated through the domain method rather than issued as an
        // ExecuteUpdate: SetAgency stamps UpdatedAt, and a bulk update would bypass both
        // that and the change tracker, leaving listings whose audit trail says they were
        // never modified.
        var listings = await _db.Properties
            .Where(p => p.AgencyId == agencyId)
            .ToListAsync(ct);

        foreach (var listing in listings)
        {
            listing.SetAgency(null);
        }

        return listings.Count;
    }
}
