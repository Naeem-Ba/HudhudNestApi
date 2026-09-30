using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Moq;
using HudhudNestApi.Infrastructure.Caching;

namespace HudhudNestApi.Auth.Tests.Application.Caching;

[Trait("Category", "RedisLookupCache")]
public sealed class DistributedCacheExtensionsTests
{
    [Fact]
    public async Task GetOrCreateAsync_WhenCacheHit_ReturnsCachedValue_AndDoesNotCallFactory()
    {
        var cache = new Mock<IDistributedCache>();
        var cached = JsonSerializer.SerializeToUtf8Bytes(new[] { "Berlin", "Essen" }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        cache.Setup(x => x.GetAsync("cities", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var factoryCalled = false;

        var result = await cache.Object.GetOrCreateAsync(
            "cities",
            TimeSpan.FromMinutes(10),
            _ =>
            {
                factoryCalled = true;
                return Task.FromResult<IReadOnlyList<string>>(["ShouldNotBeUsed"]);
            });

        Assert.False(factoryCalled);
        Assert.Equal(["Berlin", "Essen"], result);
        cache.Verify(x => x.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenCacheMiss_CallsFactory_AndStoresValueWithTenMinuteExpiration()
    {
        var cache = new Mock<IDistributedCache>();

        cache.Setup(x => x.GetAsync("amenities", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var result = await cache.Object.GetOrCreateAsync(
            "amenities",
            TimeSpan.FromMinutes(10),
            _ => Task.FromResult<IReadOnlyList<string>>(["Balcony", "Parking"]));

        Assert.Equal(["Balcony", "Parking"], result);

        cache.Verify(x => x.SetAsync(
            "amenities",
            It.IsAny<byte[]>(),
            It.Is<DistributedCacheEntryOptions>(options =>
                options.AbsoluteExpirationRelativeToNow == TimeSpan.FromMinutes(10)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveAsync_CanBeVerified_ForRefreshLogic()
    {
        var cache = new Mock<IDistributedCache>();

        await cache.Object.RemoveAsync("categories");

        cache.Verify(x => x.RemoveAsync("categories", It.IsAny<CancellationToken>()), Times.Once);
    }
}
