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
        {
            return SecurityStampValidationResult.Fail(
                "The token does not contain a security stamp.");
        }

        var cacheKey = BuildCacheKey(userId);

        var cachedSnapshot = await TryGetCachedSnapshotAsync(
            cacheKey,
            cancellationToken);

        if (cachedSnapshot is not null)
        {
            return ValidateSnapshot(cachedSnapshot, tokenSecurityStamp);
        }

        _logger.LogDebug(
            "Security stamp distributed cache miss for {CacheKey}. Fetching from DB.",
            cacheKey);

        var snapshot = await _reader.GetSecurityStampAsync(
            userId,
            cancellationToken);

        if (snapshot is null)
        {
            return SecurityStampValidationResult.Fail(
                "The user account is disabled.");
        }

        await TrySetCachedSnapshotAsync(
            cacheKey,
            snapshot,
            cancellationToken);

        return ValidateSnapshot(snapshot, tokenSecurityStamp);
    }

    private async Task<SecurityStampSnapshot?> TryGetCachedSnapshotAsync(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        string? cachedPayload;

        try
        {
            cachedPayload = await _cache.GetStringAsync(
                cacheKey,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Security stamp distributed cache is unavailable while reading {CacheKey}. Falling back to DB.",
                cacheKey);

            return null;
        }

        if (string.IsNullOrWhiteSpace(cachedPayload))
        {
            return null;
        }

        try
        {
            var cachedSnapshot = JsonSerializer.Deserialize<SecurityStampSnapshot>(
                cachedPayload,
                JsonOptions);

            if (cachedSnapshot is not null)
            {
                _logger.LogDebug(
                    "Security stamp distributed cache hit for {CacheKey}.",
                    cacheKey);

                return cachedSnapshot;
            }

            _logger.LogWarning(
                "Security stamp cache entry for {CacheKey} was empty or invalid. Removing cache entry.",
                cacheKey);

            await TryRemoveCachedSnapshotAsync(cacheKey, cancellationToken);

            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Security stamp cache entry for {CacheKey} could not be deserialized. Removing cache entry.",
                cacheKey);

            await TryRemoveCachedSnapshotAsync(cacheKey, cancellationToken);

            return null;
        }
    }

    private async Task TrySetCachedSnapshotAsync(
        string cacheKey,
        SecurityStampSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            var serializedSnapshot = JsonSerializer.Serialize(
                snapshot,
                JsonOptions);

            await _cache.SetStringAsync(
                cacheKey,
                serializedSnapshot,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheDuration
                },
                cancellationToken);

            _logger.LogDebug(
                "Security stamp snapshot cached for {CacheKey}.",
                cacheKey);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Security stamp distributed cache is unavailable while writing {CacheKey}. Continuing without cache.",
                cacheKey);
        }
    }

    private async Task TryRemoveCachedSnapshotAsync(
        string cacheKey,
        CancellationToken cancellationToken)
    {
        try
        {
            await _cache.RemoveAsync(cacheKey, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Security stamp distributed cache is unavailable while removing {CacheKey}. Continuing without cache.",
                cacheKey);
        }
    }

    private static SecurityStampValidationResult ValidateSnapshot(
        SecurityStampSnapshot snapshot,
        string tokenSecurityStamp)
    {
        if (snapshot.IsDeleted)
        {
            return SecurityStampValidationResult.Fail(
                "The user account is disabled.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.SecurityStamp) ||
            !string.Equals(
                snapshot.SecurityStamp,
                tokenSecurityStamp,
                StringComparison.Ordinal))
        {
            return SecurityStampValidationResult.Fail(
                "The token is no longer valid.");
        }

        return SecurityStampValidationResult.Success();
    }

    private static string BuildCacheKey(Guid userId)
    {
        return SecurityStampCacheKeys.ForUser(userId);
    }
}