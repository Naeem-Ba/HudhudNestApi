using System.Globalization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Auth.Interfaces;

namespace HudhudNestApi.Infrastructure.Auth.Services;

/// <summary>
/// <see cref="IEmailConfirmationSendLimiter"/> over the shared distributed cache (Redis in
/// Production, so the count survives restarts and is the same on every instance).
///
/// One entry per account holds the Unix times of the sends still inside the window; the window is
/// rolling, so "3 per 24 hours" means any 24 hours, not "until midnight UTC".
///
/// IDistributedCache has no atomic increment, so two simultaneous requests can both read the same
/// list and both be allowed: at worst a fourth mail goes out. That is a deliberate trade for not
/// adding a table and a migration to a soft cap whose job is to stop a person pressing a button
/// repeatedly. It fails open: if the cache is unreachable the mail is still sent, because blocking
/// account confirmation on a cache outage would only add to the outage.
/// </summary>
public sealed class DistributedEmailConfirmationSendLimiter : IEmailConfirmationSendLimiter
{
    private readonly IDistributedCache _cache;
    private readonly TimeProvider _clock;
    private readonly ILogger<DistributedEmailConfirmationSendLimiter> _logger;

    public DistributedEmailConfirmationSendLimiter(
        IDistributedCache cache,
        TimeProvider clock,
        ILogger<DistributedEmailConfirmationSendLimiter> logger)
    {
        _cache = cache;
        _clock = clock;
        _logger = logger;
    }

    public Task<TimeSpan?> TryAcquireAsync(Guid identityId, CancellationToken ct = default) =>
        EvaluateAsync(identityId, record: true, ct);

    public Task<TimeSpan?> PeekAsync(Guid identityId, CancellationToken ct = default) =>
        EvaluateAsync(identityId, record: false, ct);

    private async Task<TimeSpan?> EvaluateAsync(Guid identityId, bool record, CancellationToken ct)
    {
        try
        {
            var key = $"email-confirmation-sends:{identityId:N}";
            var now = _clock.GetUtcNow().ToUnixTimeSeconds();
            var windowSeconds = (long)EmailConfirmationSendPolicy.Window.TotalSeconds;

            var sends = Parse(await _cache.GetStringAsync(key, ct))
                .Where(sentAt => sentAt > now - windowSeconds)
                .OrderBy(sentAt => sentAt)
                .ToList();

            if (sends.Count >= EmailConfirmationSendPolicy.MaxSends)
            {
                // The send that frees a slot is the one that falls out of the window first.
                var oldestOfTheLastMax = sends[sends.Count - EmailConfirmationSendPolicy.MaxSends];
                return TimeSpan.FromSeconds(Math.Max(1, oldestOfTheLastMax + windowSeconds - now));
            }

            if (record)
            {
                sends.Add(now);
                await _cache.SetStringAsync(
                    key,
                    string.Join(',', sends.Select(sentAt => sentAt.ToString(CultureInfo.InvariantCulture))),
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = EmailConfirmationSendPolicy.Window
                    },
                    ct);
            }

            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Confirmation e-mail limiter could not reach the cache for identity {IdentityId}; allowing the send.",
                identityId);

            return null;
        }
    }

    private static IEnumerable<long> Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            yield break;
        }

        foreach (var part in stored.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var sentAt))
            {
                yield return sentAt;
            }
        }
    }
}
