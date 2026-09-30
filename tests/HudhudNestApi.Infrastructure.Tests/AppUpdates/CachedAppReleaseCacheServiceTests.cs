using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.AppUpdates.DTOs;
using HudhudNestApi.Application.AppUpdates.Interfaces;
using HudhudNestApi.Domain.AppUpdates.Enums;
using HudhudNestApi.Infrastructure.AppUpdates;
using HudhudNestApi.Infrastructure.Caching;

namespace HudhudNestApi.Infrastructure.Tests.AppUpdates;

/// <summary>
/// This is the only place the Redis-outage fallback can actually be exercised: integration
/// tests run against AddDistributedMemoryCache(), which never throws, so only a mocked
/// IDistributedCache can simulate a real cache failure. The feature's hard requirement — an
/// update check must never break because Redis is down — is proven here.
/// </summary>
public sealed class CachedAppReleaseCacheServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string CacheKey = "app-update:effective-release:Android";

    private static EffectiveAppReleaseDto SampleRelease() =>
        new("1.1.0", "1.0.0", "https://example.com/store", "ar", "en", "de", DateTime.UtcNow);

    private static byte[] SerializeWrapper(EffectiveAppReleaseDto? release) =>
        JsonSerializer.SerializeToUtf8Bytes(new { release }, JsonOptions);

    [Fact]
    public async Task GetEffectiveReleaseAsync_CacheHit_ReturnsFromCache_NeverHitsRepository()
    {
        var cache = new Mock<IDistributedCache>();
        cache.Setup(x => x.GetAsync(CacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(SerializeWrapper(SampleRelease()));

        var repository = new Mock<IAppReleaseRepository>();
        var service = new CachedAppReleaseCacheService(
            repository.Object, cache.Object, NullLogger<CachedAppReleaseCacheService>.Instance);

        var result = await service.GetEffectiveReleaseAsync(AppPlatform.Android);

        Assert.NotNull(result);
        Assert.Equal("1.1.0", result!.Version);
        repository.Verify(x => x.GetEffectiveReleaseAsync(It.IsAny<AppPlatform>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetEffectiveReleaseAsync_CacheMiss_LoadsFromRepositoryAndPopulatesCache()
    {
        var cache = new Mock<IDistributedCache>();
        cache.Setup(x => x.GetAsync(CacheKey, It.IsAny<CancellationToken>())).ReturnsAsync((byte[]?)null);

        var release = SampleRelease();
        var repository = new Mock<IAppReleaseRepository>();
        repository.Setup(x => x.GetEffectiveReleaseAsync(AppPlatform.Android, It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);

        var service = new CachedAppReleaseCacheService(
            repository.Object, cache.Object, NullLogger<CachedAppReleaseCacheService>.Instance);

        var result = await service.GetEffectiveReleaseAsync(AppPlatform.Android);

        Assert.Equal(release, result);
        cache.Verify(x => x.SetAsync(CacheKey, It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEffectiveReleaseAsync_NoReleaseConfigured_StillCachesTheAbsence()
    {
        var cache = new Mock<IDistributedCache>();
        cache.Setup(x => x.GetAsync(CacheKey, It.IsAny<CancellationToken>())).ReturnsAsync((byte[]?)null);

        var repository = new Mock<IAppReleaseRepository>();
        repository.Setup(x => x.GetEffectiveReleaseAsync(AppPlatform.Android, It.IsAny<CancellationToken>()))
            .ReturnsAsync((EffectiveAppReleaseDto?)null);

        var service = new CachedAppReleaseCacheService(
            repository.Object, cache.Object, NullLogger<CachedAppReleaseCacheService>.Instance);

        var result = await service.GetEffectiveReleaseAsync(AppPlatform.Android);

        Assert.Null(result);
        // The absence itself is wrapped and stored, not skipped — proves a platform with no
        // release configured yet doesn't re-hit the database on every single request once
        // Redis is healthy.
        cache.Verify(x => x.SetAsync(CacheKey, It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEffectiveReleaseAsync_CacheThrows_FallsBackToRepository_ReturnsRepositoryResult()
    {
        var cache = new Mock<IDistributedCache>();
        cache.Setup(x => x.GetAsync(CacheKey, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis connection refused"));

        var release = SampleRelease();
        var repository = new Mock<IAppReleaseRepository>();
        repository.Setup(x => x.GetEffectiveReleaseAsync(AppPlatform.Android, It.IsAny<CancellationToken>()))
            .ReturnsAsync(release);

        var service = new CachedAppReleaseCacheService(
            repository.Object, cache.Object, NullLogger<CachedAppReleaseCacheService>.Instance);

        var result = await service.GetEffectiveReleaseAsync(AppPlatform.Android);

        Assert.Equal(release, result);
        repository.Verify(x => x.GetEffectiveReleaseAsync(AppPlatform.Android, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetEffectiveReleaseAsync_CacheThrowsAndRepositoryThrows_ReturnsNull_NeverThrows()
    {
        var cache = new Mock<IDistributedCache>();
        cache.Setup(x => x.GetAsync(CacheKey, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis connection refused"));

        var repository = new Mock<IAppReleaseRepository>();
        repository.Setup(x => x.GetEffectiveReleaseAsync(AppPlatform.Android, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection refused"));

        var service = new CachedAppReleaseCacheService(
            repository.Object, cache.Object, NullLogger<CachedAppReleaseCacheService>.Instance);

        var result = await service.GetEffectiveReleaseAsync(AppPlatform.Android);

        Assert.Null(result);
    }

    [Fact]
    public async Task InvalidateAsync_CallsCacheRemove_WithCorrectKey()
    {
        var cache = new Mock<IDistributedCache>();
        var service = new CachedAppReleaseCacheService(
            Mock.Of<IAppReleaseRepository>(), cache.Object, NullLogger<CachedAppReleaseCacheService>.Instance);

        await service.InvalidateAsync(AppPlatform.Android);

        cache.Verify(x => x.RemoveAsync(CacheKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateAsync_WhenCacheThrows_DoesNotThrow()
    {
        var cache = new Mock<IDistributedCache>();
        cache.Setup(x => x.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis connection refused"));

        var service = new CachedAppReleaseCacheService(
            Mock.Of<IAppReleaseRepository>(), cache.Object, NullLogger<CachedAppReleaseCacheService>.Instance);

        await service.InvalidateAsync(AppPlatform.Android);
    }
}
