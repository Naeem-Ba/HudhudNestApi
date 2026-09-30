using HudhudNestApi.Application.AppUpdates.DTOs;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Domain.AppUpdates.Entities;
using HudhudNestApi.Domain.AppUpdates.Enums;

namespace HudhudNestApi.Application.AppUpdates.Interfaces;

public interface IAppReleaseRepository
{
    Task<AppRelease?> GetByIdAsync(Guid id, CancellationToken ct = default);

    void Add(AppRelease release);

    Task<bool> ExistsEnabledForPlatformAndVersionAsync(
        AppPlatform platform, string version, Guid? excludeId, CancellationToken ct = default);

    Task<PagedResult<AppReleaseAdminDto>> GetAdminListAsync(
        AdminAppReleaseFilterDto filter, CancellationToken ct = default);

    /// <summary>Uncached read of the effective (highest-Version, enabled, non-deleted) release
    /// for a platform, or null if none exists yet. Callers that need caching/fallback go through
    /// <see cref="IAppReleaseCacheService"/> instead of calling this directly.</summary>
    Task<EffectiveAppReleaseDto?> GetEffectiveReleaseAsync(AppPlatform platform, CancellationToken ct = default);
}
