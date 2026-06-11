using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace PropertyApi.Infrastructure.Caching;

public static class DistributedCacheExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<T> GetOrCreateAsync<T>(
        this IDistributedCache cache,
        string key,
        TimeSpan absoluteExpirationRelativeToNow,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken ct = default)
    {
        var cachedBytes = await cache.GetAsync(key, ct);
        if (cachedBytes is { Length: > 0 })
        {
            var cachedValue = JsonSerializer.Deserialize<T>(cachedBytes, JsonOptions);
            if (cachedValue is not null)
            {
                return cachedValue;
            }
        }

        var value = await factory(ct);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        await cache.SetAsync(
            key,
            bytes,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow
            },
            ct);

        return value;
    }
}
