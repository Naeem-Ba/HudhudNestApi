using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.AppUpdates.DTOs;
using HudhudNestApi.Application.AppUpdates.Interfaces;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Domain.AppUpdates.Entities;
using HudhudNestApi.Domain.AppUpdates.Enums;
using HudhudNestApi.Domain.AppUpdates.ValueObjects;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.AppUpdates;

public sealed class AppReleaseRepository : IAppReleaseRepository
{
    private readonly AppDbContext _db;

    public AppReleaseRepository(AppDbContext db) => _db = db;

    public Task<AppRelease?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.AppReleases.FirstOrDefaultAsync(r => r.Id == id, ct);

    public void Add(AppRelease release) => _db.AppReleases.Add(release);

    public Task<bool> ExistsEnabledForPlatformAndVersionAsync(
        AppPlatform platform, string version, Guid? excludeId, CancellationToken ct = default) =>
        _db.AppReleases.AnyAsync(r =>
            r.Platform == platform && r.Version == version && r.IsEnabled &&
            (excludeId == null || r.Id != excludeId), ct);

    public async Task<PagedResult<AppReleaseAdminDto>> GetAdminListAsync(
        AdminAppReleaseFilterDto filter, CancellationToken ct = default)
    {
        var query = _db.AppReleases.AsNoTracking().AsQueryable();

        if (filter.Platform.HasValue)
            query = query.Where(r => r.Platform == filter.Platform.Value);

        if (filter.IsEnabled.HasValue)
            query = query.Where(r => r.IsEnabled == filter.IsEnabled.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(r => new AppReleaseAdminDto(
                r.Id, r.Platform, r.Version, r.MinimumSupportedVersion, r.StoreUrl,
                r.ReleaseNotesAr, r.ReleaseNotesEn, r.ReleaseNotesDe, r.ReleaseDate, r.IsEnabled,
                r.CreatedAt, r.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResult<AppReleaseAdminDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = filter.Page,
            PageSize = filter.PageSize,
        };
    }

    /// <summary>
    /// The effective release for a platform is the enabled, non-deleted row with the highest
    /// Version, compared numerically (never by ReleaseDate — see AppRelease's class doc for why
    /// Version is the reliable ordering key). The candidate set per platform is always small (a
    /// handful of shipped releases), so the numeric ordering is deliberately finished in memory
    /// via AppVersion rather than attempted as a SQL string sort — "1.10.0" would incorrectly
    /// sort before "1.9.9" lexicographically. Do not "optimize" this into a SQL-only ORDER BY.
    /// </summary>
    public async Task<EffectiveAppReleaseDto?> GetEffectiveReleaseAsync(AppPlatform platform, CancellationToken ct = default)
    {
        var candidates = await _db.AppReleases.AsNoTracking()
            .Where(r => r.Platform == platform && r.IsEnabled)
            .ToListAsync(ct);

        var winner = candidates
            .OrderByDescending(r => AppVersion.Parse(r.Version))
            .ThenByDescending(r => r.ReleaseDate)
            .ThenByDescending(r => r.UpdatedAt)
            .FirstOrDefault();

        return winner is null
            ? null
            : new EffectiveAppReleaseDto(
                winner.Version, winner.MinimumSupportedVersion, winner.StoreUrl,
                winner.ReleaseNotesAr, winner.ReleaseNotesEn, winner.ReleaseNotesDe, winner.ReleaseDate);
    }
}
