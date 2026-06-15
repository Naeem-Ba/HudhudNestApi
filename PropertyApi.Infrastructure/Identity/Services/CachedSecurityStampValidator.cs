using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Security;

namespace PropertyApi.Infrastructure.Identity.Services;

public sealed class CachedSecurityStampValidator : IUserSecurityStampValidator
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly IUserSecurityStampReader _reader;
    private readonly ILogger<CachedSecurityStampValidator> _logger;

    public CachedSecurityStampValidator(
        IDistributedCache cache,
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
        var cachedPayload = await _cache.GetStringAsync(cacheKey, cancellationToken);

        if (!string.IsNullOrWhiteSpace(cachedPayload))
        {
            var cachedSnapshot = JsonSerializer.Deserialize<SecurityStampSnapshot>(
                cachedPayload,
                JsonOptions);

            if (cachedSnapshot is not null)
            {
                _logger.LogDebug("Security stamp distributed cache hit for {CacheKey}", cacheKey);
                return ValidateSnapshot(cachedSnapshot, tokenSecurityStamp);
            }

            _logger.LogWarning(
                "Security stamp cache entry for {CacheKey} could not be deserialized. Removing cache entry.",
                cacheKey);

            await _cache.RemoveAsync(cacheKey, cancellationToken);
        }

        _logger.LogDebug("Security stamp distributed cache miss for {CacheKey}. Fetching from DB.", cacheKey);

        var snapshot = await _reader.GetSecurityStampAsync(userId, cancellationToken);

        if (snapshot is null)
            return SecurityStampValidationResult.Fail("The user account is disabled.");

        var serializedSnapshot = JsonSerializer.Serialize(snapshot, JsonOptions);

        await _cache.SetStringAsync(
            cacheKey,
            serializedSnapshot,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration
            },
            cancellationToken);

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

    private static string BuildCacheKey(Guid userId) => SecurityStampCacheKeys.ForUser(userId);
}
