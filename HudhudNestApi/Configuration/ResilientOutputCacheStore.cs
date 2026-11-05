using Microsoft.AspNetCore.OutputCaching;

namespace HudhudNestApi.Configuration;

/// <summary>
/// Makes a remote output-cache store (Redis) fail open: when the store is unreachable the cache is skipped and the
/// request is answered from PostgreSQL, instead of every request waiting for the store's timeouts.
///
/// Why it exists (Phase 18 load test, two instances, <c>OutputCache:Store=Redis</c>): with Redis stopped every cached
/// public read took about 11.5 s (a read timeout of 5 s, then a write timeout of 5 s) although the answer was still
/// 200, so 10 concurrent users saw about one response per second. Worse, <c>AppDbContext.SaveChangesAsync</c> evicts the
/// property tag after committing, so a property write returned 500 for a row that had in fact been saved.
///
/// Behaviour: a failure or timeout of the store counts against a circuit. After <see cref="FailureThreshold"/>
/// consecutive failures the circuit opens for <see cref="CooldownPeriod"/>; while open, reads are misses, writes and
/// evictions are skipped and nothing waits on Redis. After the cooldown the next call is a probe: success closes the
/// circuit, failure re-opens it. Evictions skipped while open can leave an entry written before the outage until its own
/// TTL ends (30 to 60 s, the same bound the memory store has across instances).
/// </summary>
public sealed class ResilientOutputCacheStore : IOutputCacheStore
{
    public const int FailureThreshold = 2;
    public static readonly TimeSpan CooldownPeriod = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan OperationTimeout = TimeSpan.FromMilliseconds(750);

    private readonly IOutputCacheStore _inner;
    private readonly ILogger<ResilientOutputCacheStore> _logger;
    private readonly TimeProvider _time;
    private readonly TimeSpan _operationTimeout;
    private readonly object _gate = new();

    private int _consecutiveFailures;
    private DateTimeOffset _openUntil = DateTimeOffset.MinValue;

    public ResilientOutputCacheStore(
        IOutputCacheStore inner,
        ILogger<ResilientOutputCacheStore> logger,
        TimeProvider? time = null,
        TimeSpan? operationTimeout = null)
    {
        _inner = inner;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _operationTimeout = operationTimeout ?? OperationTimeout;
    }

    /// <summary>True while calls are being skipped (the circuit is open and its cooldown has not ended).</summary>
    public bool IsOpen
    {
        get
        {
            lock (_gate) return _time.GetUtcNow() < _openUntil;
        }
    }

    public async ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (IsOpen) return null;

        try
        {
            var value = await _inner.GetAsync(key, cancellationToken).AsTask().WaitAsync(_operationTimeout, _time, cancellationToken);
            RecordSuccess();
            return value;
        }
        catch (Exception ex) when (ShouldSwallow(ex, cancellationToken))
        {
            RecordFailure(ex, "read");
            return null;
        }
    }

    public async ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken)
    {
        if (IsOpen) return;

        try
        {
            await _inner.SetAsync(key, value, tags, validFor, cancellationToken).AsTask().WaitAsync(_operationTimeout, _time, cancellationToken);
            RecordSuccess();
        }
        catch (Exception ex) when (ShouldSwallow(ex, cancellationToken))
        {
            RecordFailure(ex, "write");
        }
    }

    public async ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
    {
        if (IsOpen) return;

        try
        {
            await _inner.EvictByTagAsync(tag, cancellationToken).AsTask().WaitAsync(_operationTimeout, _time, cancellationToken);
            RecordSuccess();
        }
        catch (Exception ex) when (ShouldSwallow(ex, cancellationToken))
        {
            RecordFailure(ex, "eviction");
        }
    }

    // A caller that cancelled its own request is not a store failure.
    private static bool ShouldSwallow(Exception ex, CancellationToken callerToken) =>
        !(ex is OperationCanceledException && callerToken.IsCancellationRequested);

    private void RecordSuccess()
    {
        lock (_gate)
        {
            if (_consecutiveFailures > 0)
                _logger.LogInformation("Output cache store answered again; circuit closed.");
            _consecutiveFailures = 0;
            _openUntil = DateTimeOffset.MinValue;
        }
    }

    private void RecordFailure(Exception ex, string operation)
    {
        lock (_gate)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= FailureThreshold)
            {
                var wasOpen = _time.GetUtcNow() < _openUntil;
                _openUntil = _time.GetUtcNow() + CooldownPeriod;
                if (!wasOpen)
                {
                    _logger.LogWarning(
                        ex,
                        "Output cache store failed ({Operation}) {Failures} times in a row; skipping it for {Cooldown} s and serving from the database.",
                        operation, _consecutiveFailures, (int)CooldownPeriod.TotalSeconds);
                }
            }
            else
            {
                _logger.LogWarning(ex, "Output cache store failed ({Operation}); the request is served without the cache.", operation);
            }
        }
    }
}
