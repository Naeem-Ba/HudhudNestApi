using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.AppUpdates.DTOs;
using HudhudNestApi.Application.AppUpdates.Interfaces;
using HudhudNestApi.Domain.AppUpdates.Enums;
using HudhudNestApi.Infrastructure.Caching;

namespace HudhudNestApi.Infrastructure.AppUpdates;

/// <summary>
/// Wraps IAppReleaseRepository.GetEffectiveReleaseAsync with caching AND an explicit
/// Redis-outage fallback, scoped to this one feature only — DistributedCacheExtensions.
/// GetOrCreateAsync itself is left unmodified. This is the hottest read in the whole feature
/// (hit on every app launch) and per the feature's requirements must never let a cache/DB
/// problem surface as an app-launch failure.
/// </summary>
public sealed class CachedAppReleaseCacheService : IAppReleaseCacheService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IAppReleaseRepository _repository;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedAppReleaseCacheService> _logger;

    public CachedAppReleaseCacheService(
        IAppReleaseRepository repository, IDistributedCache cache, ILogger<CachedAppReleaseCacheService> logger)
    {
        _repository = repository;
        _cache = cache;
        _logger = logger;
    }

    public async Task<EffectiveAppReleaseDto?> GetEffectiveReleaseAsync(AppPlatform platform, CancellationToken ct = default)
    {
        var cacheKey = AppUpdateCacheKeys.EffectiveRelease(platform);

        try
        {
            // Wrapped in a sentinel record so "no release configured for this platform" is
            // itself a cacheable, positive result — distinguishable from a cache miss, so an
            // empty platform doesn't re-hit the database on every single check call.
            var cached = await _cache.GetOrCreateAsync(
                cacheKey,
                CacheDuration,
                async token => new CachedEffectiveRelease(await _repository.GetEffectiveReleaseAsync(platform, token)),
                ct);

            return cached.Release;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Distributed cache unavailable while resolving the effective app release for {Platform}. Falling back to a direct database read.",
                platform);

            try
            {
                return await _repository.GetEffectiveReleaseAsync(platform, ct);
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx,
                    "Database fallback also failed while resolving the effective app release for {Platform}. Reporting no update available.",
                    platform);
                return null;
            }
        }
    }

    public async Task InvalidateAsync(AppPlatform platform, CancellationToken ct = default)
    {
        try
        {
            await _cache.RemoveAsync(AppUpdateCacheKeys.EffectiveRelease(platform), ct);
        }
        catch (Exception ex)
        {
            // Best-effort: while Redis is down, GetEffectiveReleaseAsync's own try/catch above
            // already bypasses the cache entirely, so a failed invalidation here must not fail
            // the admin's create/update/enable/disable/delete request.
            _logger.LogWarning(ex, "Failed to invalidate the app release cache for {Platform}.", platform);
        }
    }

    private sealed record CachedEffectiveRelease(EffectiveAppReleaseDto? Release);
}
