using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HudhudNestApi.Infrastructure.Identity.Services;

namespace HudhudNestApi.Auth.Tests.Application.Security;

public sealed class DistributedSecurityStampCacheInvalidatorTests
{
    [Fact]
    [Trait("Category", "SecurityStampCache")]
    public async Task InvalidateAsync_RemovesSecurityStampCacheEntry()
    {
        var userId = Guid.NewGuid();
        var cache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));

        var key = $"securitystamp:{userId:N}";
        await cache.SetStringAsync(key, "cached-stamp");

        var invalidator = new DistributedSecurityStampCacheInvalidator(
            cache,
            NullLogger<DistributedSecurityStampCacheInvalidator>.Instance);

        await invalidator.InvalidateAsync(userId);

        var cachedValue = await cache.GetStringAsync(key);
        Assert.Null(cachedValue);
    }
}
