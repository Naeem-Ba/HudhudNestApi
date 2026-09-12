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

    public async Task<IReadOnlyList<Agency>> GetByIdsAsync(
        IReadOnlyCollection<Guid> agencyIds,
        CancellationToken ct = default)
    {
        if (agencyIds.Count == 0)
            return [];

        return await _db.Agencies
            .AsNoTracking()
            .Where(a => agencyIds.Contains(a.Id))
            .ToListAsync(ct);
    }

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
        // !u.IsDeleted (Finding F2): a deleted member's account is anonymized, not removed --
        // without this, "Deleted User" would keep appearing in and counting toward the
        // agency's member list forever. See UserAccount.IsDeleted's doc comment for why this
        // is a scoped filter here rather than a global EF Core query filter.
        => await _db.UserAccounts
            .AsNoTracking()
            .Where(u => u.AgencyId == agencyId && !u.IsDeleted)
            .OrderBy(u => u.AgencyJoinedAt)
            .ToListAsync(ct);

    public async Task<int> CountMembersAsync(Guid agencyId, CancellationToken ct = default)
        // See GetMembersAsync above for why !u.IsDeleted is required here too -- this count
        // feeds agency-size/business logic and must not include anonymized former members.
        => await _db.UserAccounts
            .AsNoTracking()
            .CountAsync(u => u.AgencyId == agencyId && !u.IsDeleted, ct);

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

    public async Task<IReadOnlyList<Agency>> FindActiveByLocationAsync(
        int? neighborhoodId,
        int? districtId,
        IReadOnlyCollection<int>? governorateIds,
        IReadOnlyCollection<Guid> excludeAgencyIds,
        CancellationToken ct = default)
        => await ApplyActiveLocationFilter(
                _db.Agencies.AsNoTracking(),
                neighborhoodId,
                districtId,
                governorateIds,
                excludeAgencyIds)
            .ToListAsync(ct);

    /// <summary>
    /// The actual DB-side matching rule behind <see cref="FindActiveByLocationAsync"/>,
    /// pulled out as a public static IQueryable transform — same pattern as
    /// PropertyRepository.ApplyFilter/ApplyComparableListingsFilter — so
    /// PropertyApi.Architecture.Tests can exercise the real filtering logic over an
    /// in-memory List&lt;Agency&gt;.AsQueryable() with no database involved.
    /// </summary>
    public static IQueryable<Agency> ApplyActiveLocationFilter(
        IQueryable<Agency> query,
        int? neighborhoodId,
        int? districtId,
        IReadOnlyCollection<int>? governorateIds,
        IReadOnlyCollection<Guid> excludeAgencyIds)
    {
        query = query.Where(a => a.IsActive);

        if (excludeAgencyIds.Count > 0)
            query = query.Where(a => !excludeAgencyIds.Contains(a.Id));

        if (neighborhoodId.HasValue)
            return query.Where(a => a.NeighborhoodId == neighborhoodId.Value);

        if (districtId.HasValue)
            return query.Where(a => a.DistrictId == districtId.Value);

        if (governorateIds is { Count: > 0 })
            return query.Where(a => a.GovernorateId != null && governorateIds.Contains(a.GovernorateId.Value));

        // No scope given at all — never return "every active agency" by accident.
        return query.Where(_ => false);
    }
}
