using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Infrastructure.Auth.Services;

namespace HudhudNestApi.Infrastructure.Tests.Auth;

/// <summary>
/// "At most 3 confirmation mails per account per 24 hours" -- the rule the HTTP rate limiter could not
/// enforce (the add-email endpoint had no limit at all). The window is rolling, the count is per account,
/// and a cache outage must not stop a mail.
/// </summary>
public sealed class DistributedEmailConfirmationSendLimiterTests
{
    private static readonly TimeSpan Window = EmailConfirmationSendPolicy.Window;

    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero));
    private readonly InMemoryDistributedCache _cache = new();

    private DistributedEmailConfirmationSendLimiter Create(IDistributedCache? cache = null) =>
        new(cache ?? _cache, _clock, NullLogger<DistributedEmailConfirmationSendLimiter>.Instance);

    [Fact(DisplayName = "The first three sends in 24 hours are allowed, the fourth is refused")]
    public async Task FourthSend_IsRefused()
    {
        var limiter = Create();
        var account = Guid.NewGuid();

        for (var send = 1; send <= EmailConfirmationSendPolicy.MaxSends; send++)
        {
            Assert.Null(await limiter.TryAcquireAsync(account));
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.NotNull(await limiter.TryAcquireAsync(account));
    }

    [Fact(DisplayName = "A refused send reports when the oldest one leaves the window")]
    public async Task Refusal_ReportsTimeUntilTheOldestSendExpires()
    {
        var limiter = Create();
        var account = Guid.NewGuid();

        await limiter.TryAcquireAsync(account);                 // t = 0
        _clock.Advance(TimeSpan.FromHours(1));
        await limiter.TryAcquireAsync(account);                 // t = 1 h
        _clock.Advance(TimeSpan.FromHours(1));
        await limiter.TryAcquireAsync(account);                 // t = 2 h

        _clock.Advance(TimeSpan.FromHours(1));                  // t = 3 h
        var retryAfter = await limiter.TryAcquireAsync(account);

        // The first send (t = 0) leaves the window at t = 24 h, i.e. 21 h from now.
        Assert.Equal(TimeSpan.FromHours(21), retryAfter);
    }

    [Fact(DisplayName = "The window rolls: one slot frees up as soon as the oldest send is 24 hours old")]
    public async Task WindowRolls_OneSlotFreesWhenTheOldestSendExpires()
    {
        var limiter = Create();
        var account = Guid.NewGuid();

        await limiter.TryAcquireAsync(account);                 // t = 0
        _clock.Advance(TimeSpan.FromHours(10));
        await limiter.TryAcquireAsync(account);                 // t = 10 h
        await limiter.TryAcquireAsync(account);                 // t = 10 h
        Assert.NotNull(await limiter.TryAcquireAsync(account)); // full

        _clock.Advance(Window - TimeSpan.FromHours(10) + TimeSpan.FromSeconds(1)); // t = 24 h + 1 s

        Assert.Null(await limiter.TryAcquireAsync(account));    // the t = 0 send has expired
        Assert.NotNull(await limiter.TryAcquireAsync(account)); // the two at t = 10 h have not
    }

    [Fact(DisplayName = "Peek never uses up a slot")]
    public async Task Peek_DoesNotCount()
    {
        var limiter = Create();
        var account = Guid.NewGuid();

        for (var i = 0; i < 10; i++)
        {
            Assert.Null(await limiter.PeekAsync(account));
        }

        for (var send = 1; send <= EmailConfirmationSendPolicy.MaxSends; send++)
        {
            Assert.Null(await limiter.TryAcquireAsync(account));
        }

        Assert.NotNull(await limiter.PeekAsync(account));
    }

    [Fact(DisplayName = "The limit is per account: one account's sends do not use another's quota")]
    public async Task Accounts_AreCountedSeparately()
    {
        var limiter = Create();
        var busy = Guid.NewGuid();
        var other = Guid.NewGuid();

        for (var send = 1; send <= EmailConfirmationSendPolicy.MaxSends; send++)
        {
            await limiter.TryAcquireAsync(busy);
        }

        Assert.NotNull(await limiter.TryAcquireAsync(busy));
        Assert.Null(await limiter.TryAcquireAsync(other));
    }

    [Fact(DisplayName = "A cache outage never blocks a confirmation mail")]
    public async Task CacheFailure_FailsOpen()
    {
        var limiter = Create(new ThrowingDistributedCache());
        var account = Guid.NewGuid();

        Assert.Null(await limiter.PeekAsync(account));
        Assert.Null(await limiter.TryAcquireAsync(account));
    }

    [Fact(DisplayName = "Cancellation is not swallowed as a cache failure")]
    public async Task Cancellation_Propagates()
    {
        var limiter = Create(new CancellingDistributedCache());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => limiter.TryAcquireAsync(Guid.NewGuid()));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualClock(DateTimeOffset start) => _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private class InMemoryDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _entries = new();

        public virtual Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            Task.FromResult(_entries.TryGetValue(key, out var value) ? value : null);

        public virtual Task SetAsync(
            string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            _entries[key] = value;
            return Task.CompletedTask;
        }

        public byte[]? Get(string key) => _entries.TryGetValue(key, out var value) ? value : null;

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _entries[key] = value;

        public void Refresh(string key) { }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _entries.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            _entries.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingDistributedCache : InMemoryDistributedCache
    {
        public override Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            throw new InvalidOperationException("cache is down");

        public override Task SetAsync(
            string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            throw new InvalidOperationException("cache is down");
    }

    private sealed class CancellingDistributedCache : InMemoryDistributedCache
    {
        public override Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            throw new OperationCanceledException();
    }
}
