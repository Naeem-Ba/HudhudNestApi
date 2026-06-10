using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Common.Security;
using PropertyApi.Infrastructure.Identity.Services;

namespace PropertyApi.Auth.Tests.Application.Security;

public sealed class CachedSecurityStampValidatorTests
{
    [Fact]
    [Trait("Category", "SecurityStampCache")]
    public async Task ValidateAsync_CacheMiss_FetchesFromReader_AndCachesSnapshot()
    {
        var userId = Guid.NewGuid();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var reader = new CountingSecurityStampReader(
            new SecurityStampSnapshot("stamp-1", IsDeleted: false));

        var validator = CreateValidator(cache, reader);

        var first = await validator.ValidateAsync(userId, "stamp-1");
        var second = await validator.ValidateAsync(userId, "stamp-1");

        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    [Trait("Category", "SecurityStampCache")]
    public async Task ValidateAsync_CacheHit_DoesNotFetchFromReaderAgain()
    {
        var userId = Guid.NewGuid();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var cacheKey = $"securitystamp:{userId:N}";

        cache.Set(cacheKey, new SecurityStampSnapshot("cached-stamp", IsDeleted: false));

        var reader = new CountingSecurityStampReader(
            new SecurityStampSnapshot("db-stamp", IsDeleted: false));

        var validator = CreateValidator(cache, reader);

        var result = await validator.ValidateAsync(userId, "cached-stamp");

        Assert.True(result.IsValid);
        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    [Trait("Category", "SecurityStampCache")]
    public async Task ValidateAsync_CacheExpired_FetchesFromReaderAgain()
    {
        var userId = Guid.NewGuid();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var cacheKey = $"securitystamp:{userId:N}";

        cache.Set(
            cacheKey,
            new SecurityStampSnapshot("expired-stamp", IsDeleted: false),
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(1)
            });

        await Task.Delay(30);

        var reader = new CountingSecurityStampReader(
            new SecurityStampSnapshot("fresh-stamp", IsDeleted: false));

        var validator = CreateValidator(cache, reader);

        var result = await validator.ValidateAsync(userId, "fresh-stamp");

        Assert.True(result.IsValid);
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    [Trait("Category", "SecurityStampCache")]
    public async Task ValidateAsync_StampMismatch_ReturnsInvalid()
    {
        var userId = Guid.NewGuid();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var reader = new CountingSecurityStampReader(
            new SecurityStampSnapshot("current-stamp", IsDeleted: false));

        var validator = CreateValidator(cache, reader);

        var result = await validator.ValidateAsync(userId, "old-stamp");

        Assert.False(result.IsValid);
        Assert.Equal("The token is no longer valid.", result.FailureMessage);
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    [Trait("Category", "SecurityStampCache")]
    public async Task ValidateAsync_DeletedUser_ReturnsInvalid()
    {
        var userId = Guid.NewGuid();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var reader = new CountingSecurityStampReader(
            new SecurityStampSnapshot("stamp-1", IsDeleted: true));

        var validator = CreateValidator(cache, reader);

        var result = await validator.ValidateAsync(userId, "stamp-1");

        Assert.False(result.IsValid);
        Assert.Equal("The user account is disabled.", result.FailureMessage);
    }

    [Fact]
    [Trait("Category", "SecurityStampCache")]
    public async Task ValidateAsync_UnknownUser_ReturnsInvalid_AndDoesNotCacheNull()
    {
        var userId = Guid.NewGuid();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var reader = new CountingSecurityStampReader(null);

        var validator = CreateValidator(cache, reader);

        var first = await validator.ValidateAsync(userId, "stamp-1");
        var second = await validator.ValidateAsync(userId, "stamp-1");

        Assert.False(first.IsValid);
        Assert.False(second.IsValid);
        Assert.Equal(2, reader.CallCount);
    }

    private static CachedSecurityStampValidator CreateValidator(
        IMemoryCache cache,
        IUserSecurityStampReader reader)
    {
        return new CachedSecurityStampValidator(
            cache,
            reader,
            NullLogger<CachedSecurityStampValidator>.Instance);
    }

    private sealed class CountingSecurityStampReader : IUserSecurityStampReader
    {
        private readonly SecurityStampSnapshot? _snapshot;

        public CountingSecurityStampReader(SecurityStampSnapshot? snapshot)
        {
            _snapshot = snapshot;
        }

        public int CallCount { get; private set; }

        public Task<SecurityStampSnapshot?> GetSecurityStampAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_snapshot);
        }
    }
}
