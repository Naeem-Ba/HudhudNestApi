using HudhudNestApi.Application.AppUpdates.DTOs;
using HudhudNestApi.Domain.AppUpdates.Enums;

namespace HudhudNestApi.Application.AppUpdates.Interfaces;

/// <summary>
/// Application-layer seam in front of the effective-release lookup, kept free of any
/// IDistributedCache/Infrastructure dependency so CheckAppUpdateQueryHandler stays unit-testable
/// with a plain mock. The Infrastructure implementation is what actually caches this (the
/// hottest read path in the feature — hit on every app launch) and falls back to a direct
/// database read if the cache throws.
/// </summary>
public interface IAppReleaseCacheService
{
    Task<EffectiveAppReleaseDto?> GetEffectiveReleaseAsync(AppPlatform platform, CancellationToken ct = default);

    Task InvalidateAsync(AppPlatform platform, CancellationToken ct = default);
}
