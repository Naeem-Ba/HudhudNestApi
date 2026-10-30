using System.Collections.Concurrent;

namespace HudhudNestApi.Security.RateLimiting;

/// <summary>
/// Fixed-window counter kept in this process's memory, for the anonymous public read policies
/// (launch audit A6-002). Counting those on Redis cost at least one command per public GET, which
/// would exhaust Upstash's free command quota and turn every public page into a 503. With one API
/// instance (Render free) an in-process count is exact; with several, each instance counts on
/// its own, so the effective limit is multiplied by the instance count.
/// </summary>
public sealed class InProcessFixedWindowCounter
{
    private const int SweepEvery = 4096;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private int _sinceSweep;

    /// <summary>
    /// Counts one request for <paramref name="key"/> in the window <paramref name="bucket"/>, which
    /// ends at <paramref name="windowEndsAtMs"/> (Unix milliseconds), and returns the new count.
    /// </summary>
    public long Increment(string key, long bucket, long windowEndsAtMs, long nowMs)
    {
        if (Interlocked.Increment(ref _sinceSweep) >= SweepEvery)
        {
            Interlocked.Exchange(ref _sinceSweep, 0);
            Sweep(nowMs);
        }

        while (true)
        {
            var entry = _entries.GetOrAdd(key, _ => new Entry(bucket, windowEndsAtMs));
            // ">=": a request whose clock read lands just before a window change, after another
            // request already opened the new window, counts in the new window.
            if (entry.Bucket >= bucket)
                return Interlocked.Increment(ref entry.Count);

            // The stored entry belongs to an older window: replace it with a fresh one.
            var fresh = new Entry(bucket, windowEndsAtMs);
            if (_entries.TryUpdate(key, fresh, entry))
                return Interlocked.Increment(ref fresh.Count);
        }
    }

    internal int EntryCount => _entries.Count;

    // Drops entries whose window has ended, which bounds memory to the clients seen in the
    // current windows. Comparing end times (not bucket numbers) keeps policies with different
    // window lengths apart.
    private void Sweep(long nowMs)
    {
        foreach (var pair in _entries)
        {
            if (pair.Value.WindowEndsAtMs <= nowMs)
                _entries.TryRemove(pair);
        }
    }

    private sealed class Entry(long bucket, long windowEndsAtMs)
    {
        public readonly long Bucket = bucket;
        public readonly long WindowEndsAtMs = windowEndsAtMs;
        public long Count;
    }
}
