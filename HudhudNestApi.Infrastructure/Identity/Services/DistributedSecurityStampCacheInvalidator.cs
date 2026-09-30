using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Interfaces;

namespace HudhudNestApi.Infrastructure.Identity.Services;

/// <summary>
/// Removes the cached security-stamp snapshot from the configured distributed
/// cache. In Production this is Redis via AddStackExchangeRedisCache.
/// </summary>
public sealed class DistributedSecurityStampCacheInvalidator : IUserSecurityStampCacheInvalidator
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<DistributedSecurityStampCacheInvalidator> _logger;

    public DistributedSecurityStampCacheInvalidator(
        IDistributedCache cache,
        ILogger<DistributedSecurityStampCacheInvalidator> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task InvalidateAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = SecurityStampCacheKeys.ForUser(userId);
        await _cache.RemoveAsync(cacheKey, cancellationToken);

        _logger.LogDebug(
            "Invalidated distributed security-stamp cache entry {CacheKey}.",
            cacheKey);
    }
}
