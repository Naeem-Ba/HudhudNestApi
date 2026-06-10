using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Security;

namespace PropertyApi.Infrastructure.Identity.Services;

public sealed class CachedSecurityStampValidator : IUserSecurityStampValidator
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IMemoryCache _cache;
    private readonly IUserSecurityStampReader _reader;
    private readonly ILogger<CachedSecurityStampValidator> _logger;

    public CachedSecurityStampValidator(
        IMemoryCache cache,
        IUserSecurityStampReader reader,
        ILogger<CachedSecurityStampValidator> logger)
    {
        _cache = cache;
        _reader = reader;
        _logger = logger;
    }

    public async Task<SecurityStampValidationResult> ValidateAsync(
        Guid userId,
        string tokenSecurityStamp,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenSecurityStamp))
            return SecurityStampValidationResult.Fail("The token does not contain a security stamp.");

        var cacheKey = BuildCacheKey(userId);

        if (_cache.TryGetValue(cacheKey, out SecurityStampSnapshot? cachedSnapshot) && cachedSnapshot is not null)
        {
            _logger.LogDebug("Security stamp cache hit for {CacheKey}", cacheKey);
            return ValidateSnapshot(cachedSnapshot, tokenSecurityStamp);
        }

        _logger.LogDebug("Security stamp cache miss for {CacheKey}. Fetching from DB.", cacheKey);

        var snapshot = await _reader.GetSecurityStampAsync(userId, cancellationToken);

        if (snapshot is null)
            return SecurityStampValidationResult.Fail("The user account is disabled.");

        _cache.Set(
            cacheKey,
            snapshot,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration,
                Priority = CacheItemPriority.High
            });

        return ValidateSnapshot(snapshot, tokenSecurityStamp);
    }

    private static SecurityStampValidationResult ValidateSnapshot(
        SecurityStampSnapshot snapshot,
        string tokenSecurityStamp)
    {
        if (snapshot.IsDeleted)
            return SecurityStampValidationResult.Fail("The user account is disabled.");

        if (string.IsNullOrWhiteSpace(snapshot.SecurityStamp) ||
            !string.Equals(snapshot.SecurityStamp, tokenSecurityStamp, StringComparison.Ordinal))
        {
            return SecurityStampValidationResult.Fail("The token is no longer valid.");
        }

        return SecurityStampValidationResult.Success();
    }

    private static string BuildCacheKey(Guid userId) => $"securitystamp:{userId:N}";
}
