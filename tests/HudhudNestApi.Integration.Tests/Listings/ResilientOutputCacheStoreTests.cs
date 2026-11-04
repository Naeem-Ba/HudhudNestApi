using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Configuration;

namespace HudhudNestApi.Integration.Tests.Listings;

/// <summary>
/// Phase 18 load test: with OutputCache:Store=Redis and Redis stopped, every cached read took about 11.5 s and a property
/// write returned 500 after committing. The store now fails open behind a circuit.
/// </summary>
public sealed class ResilientOutputCacheStoreTests
{
    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class FakeStore : IOutputCacheStore
    {
        public bool Down { get; set; }
        public TimeSpan? Delay { get; set; }
        public int Calls { get; private set; }
        public Dictionary<string, byte[]> Items { get; } = [];
        public List<string> Evicted { get; } = [];

        private async Task GateAsync()
        {
            Calls++;
            if (Delay is { } d) await Task.Delay(d);
            if (Down) throw new InvalidOperationException("redis is down");
        }

        public async ValueTask<byte[]?> GetAsync(string key, CancellationToken ct)
        {
            await GateAsync();
            return Items.GetValueOrDefault(key);
        }

        public async ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken ct)
        {
            await GateAsync();
            Items[key] = value;
        }

        public async ValueTask EvictByTagAsync(string tag, CancellationToken ct)
        {
            await GateAsync();
            Evicted.Add(tag);
        }
    }

    private static (ResilientOutputCacheStore Store, FakeStore Inner, ManualTime Time) Create(TimeSpan? timeout = null)
    {
        var inner = new FakeStore();
        var time = new ManualTime();
        return (new ResilientOutputCacheStore(inner, NullLogger<ResilientOutputCacheStore>.Instance, time, timeout), inner, time);
    }

    [Fact(DisplayName = "A healthy store passes reads, writes and evictions straight through")]
    public async Task Healthy_PassesThrough()
    {
        var (store, inner, _) = Create();

        await store.SetAsync("k", [1, 2], ["t"], TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Equal(new byte[] { 1, 2 }, await store.GetAsync("k", CancellationToken.None));
        await store.EvictByTagAsync("properties", CancellationToken.None);

        Assert.Equal(["properties"], inner.Evicted);
        Assert.False(store.IsOpen);
    }

    [Fact(DisplayName = "A store that throws is a cache miss, not a failed request")]
    public async Task Down_ReadIsAMiss_WriteAndEvictionAreSilent()
    {
        var (store, inner, _) = Create();
        inner.Down = true;

        Assert.Null(await store.GetAsync("k", CancellationToken.None));
        await store.SetAsync("k", [1], null, TimeSpan.FromSeconds(30), CancellationToken.None);
        await store.EvictByTagAsync("properties", CancellationToken.None);
    }

    [Fact(DisplayName = "After two failures the circuit opens and Redis is not called again until the cooldown ends")]
    public async Task Circuit_OpensAfterTwoFailures_AndSkipsTheStore()
    {
        var (store, inner, _) = Create();
        inner.Down = true;

        await store.GetAsync("a", CancellationToken.None);
        Assert.False(store.IsOpen);
        await store.GetAsync("b", CancellationToken.None);
        Assert.True(store.IsOpen);
        var callsWhenOpened = inner.Calls;

        for (var i = 0; i < 50; i++)
        {
            Assert.Null(await store.GetAsync("c", CancellationToken.None));
            await store.SetAsync("c", [1], null, TimeSpan.FromSeconds(30), CancellationToken.None);
            await store.EvictByTagAsync("properties", CancellationToken.None);
        }

        Assert.Equal(callsWhenOpened, inner.Calls);
    }

    [Fact(DisplayName = "After the cooldown one probe is sent; a recovered store closes the circuit")]
    public async Task Circuit_RecoversWhenTheStoreReturns()
    {
        var (store, inner, time) = Create();
        inner.Down = true;
        await store.GetAsync("a", CancellationToken.None);
        await store.GetAsync("b", CancellationToken.None);
        Assert.True(store.IsOpen);

        inner.Down = false;
        inner.Items["k"] = [9];
        Assert.Null(await store.GetAsync("k", CancellationToken.None));     // still open: no call

        time.Advance(ResilientOutputCacheStore.CooldownPeriod + TimeSpan.FromSeconds(1));
        Assert.False(store.IsOpen);
        Assert.Equal(new byte[] { 9 }, await store.GetAsync("k", CancellationToken.None));
        Assert.False(store.IsOpen);
    }

    [Fact(DisplayName = "A failed probe re-opens the circuit for another full cooldown")]
    public async Task Circuit_ReopensWhenTheProbeFails()
    {
        var (store, inner, time) = Create();
        inner.Down = true;
        await store.GetAsync("a", CancellationToken.None);
        await store.GetAsync("b", CancellationToken.None);

        time.Advance(ResilientOutputCacheStore.CooldownPeriod + TimeSpan.FromSeconds(1));
        await store.GetAsync("probe", CancellationToken.None);

        Assert.True(store.IsOpen);
    }

    [Fact(DisplayName = "A success between failures resets the count, so one blip does not open the circuit")]
    public async Task Success_ResetsTheFailureCount()
    {
        var (store, inner, _) = Create();

        inner.Down = true;
        await store.GetAsync("a", CancellationToken.None);
        inner.Down = false;
        await store.GetAsync("b", CancellationToken.None);
        inner.Down = true;
        await store.GetAsync("c", CancellationToken.None);

        Assert.False(store.IsOpen);
    }

    [Fact(DisplayName = "A store that hangs is cut off at the operation timeout instead of holding the request")]
    public async Task SlowStore_IsCutOff()
    {
        var (store, inner, _) = Create(timeout: TimeSpan.FromMilliseconds(100));
        inner.Delay = TimeSpan.FromSeconds(5);

        var started = DateTime.UtcNow;
        var value = await store.GetAsync("k", CancellationToken.None);

        Assert.Null(value);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2));
    }

    [Fact(DisplayName = "A caller that cancels its own request is not counted as a store failure")]
    public async Task CallerCancellation_IsNotAStoreFailure()
    {
        var (store, inner, _) = Create();
        inner.Delay = TimeSpan.FromSeconds(5);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await store.GetAsync("k", cts.Token));

        Assert.False(store.IsOpen);
    }

    [Fact(DisplayName = "The cache connection gives up in half a second and fails fast while disconnected")]
    public void CacheConnection_UsesShortTimeoutsAndFailFast()
    {
        var options = OutputCacheRegistration.BuildCacheRedisOptions("redis.invalid:6379,abortConnect=false");

        Assert.Equal(500, options.SyncTimeout);
        Assert.Equal(500, options.AsyncTimeout);
        Assert.Equal(1000, options.ConnectTimeout);
        Assert.False(options.AbortOnConnectFail);
        Assert.Same(StackExchange.Redis.BacklogPolicy.FailFast, options.BacklogPolicy);
    }
}
