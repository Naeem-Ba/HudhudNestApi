using HudhudNestApi.Application.Growth;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Referrals;
using HudhudNestApi.Domain.Growth.Entities;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Referrals.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HudhudNestApi.Infrastructure.Referrals;

/// <summary>
/// Growth engine P5: decides pending referrals from real activity, once an hour, under an advisory lock.
/// Qualified when the invited account (1) still exists and is not banned, (2) has a confirmed phone when
/// required, (3) has a listing on the market for at least N days. Rejected when it shares the referrer's phone
/// number, when either account is gone or banned, or when the referrer reached the 30-day cap. Expired after
/// the window. Nothing here grants a reward.
/// </summary>
public sealed class ReferralQualificationHostedService : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    internal const int MaxPerSweep = 500;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReferralQualificationHostedService> _logger;

    public ReferralQualificationHostedService(IServiceScopeFactory scopeFactory, ILogger<ReferralQualificationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Referral qualification sweep failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    internal sealed record SweepResult(int Qualified, int Rejected, int Expired);

    internal async Task<SweepResult> RunOnceAsync(DateTime nowUtc, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<ReferralOptions>>().Value;
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var growth = scope.ServiceProvider.GetRequiredService<IGrowthEventRecorder>();
        var result = new SweepResult(0, 0, 0);
        if (!options.Enabled)
            return result;

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await BackgroundJobLock.TryRunAsync(
                db.Database.GetDbConnection(),
                BackgroundJobLockKeys.ReferralQualification,
                nameof(ReferralQualificationHostedService),
                _logger,
                async () => result = await SweepAsync(db, options, notifications, growth, nowUtc, ct),
                ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return result;
    }

    private static async Task<SweepResult> SweepAsync(
        AppDbContext db, ReferralOptions options, INotificationService notifications, IGrowthEventRecorder growth,
        DateTime nowUtc, CancellationToken ct)
    {
        var pending = await db.Referrals
            .Where(r => r.Status == ReferralStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .Take(MaxPerSweep)
            .ToListAsync(ct);
        if (pending.Count == 0)
            return new SweepResult(0, 0, 0);

        var userIds = pending.SelectMany(r => new[] { r.ReferrerUserId, r.ReferredUserId }).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.IsDeleted, u.IsBanned, u.PhoneNumberConfirmed, u.PhoneNumberLookupHash })
            .ToDictionaryAsync(u => u.Id, ct);

        var listedBefore = nowUtc.AddDays(-Math.Max(0, options.QualifyingListingDays));
        var referredIds = pending.Select(r => r.ReferredUserId).ToList();
        var withListing = (await db.Properties.AsNoTracking()
                .Where(Property.OnMarket(nowUtc))
                .Where(p => referredIds.Contains(p.OwnerId) && p.PublishedAt != null && p.PublishedAt <= listedBefore)
                .Select(p => p.OwnerId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        var capSince = nowUtc.AddDays(-30);
        var referrerIds = pending.Select(r => r.ReferrerUserId).Distinct().ToList();
        var qualifiedRecently = await db.Referrals.AsNoTracking()
            .Where(r => referrerIds.Contains(r.ReferrerUserId) && r.Status == ReferralStatus.Qualified && r.DecidedAt >= capSince)
            .GroupBy(r => r.ReferrerUserId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

        int qualified = 0, rejected = 0, expired = 0;
        var newlyQualified = new List<Referral>();

        foreach (var r in pending)
        {
            users.TryGetValue(r.ReferredUserId, out var referred);
            users.TryGetValue(r.ReferrerUserId, out var referrer);

            if (referred is null || referred.IsDeleted || referred.IsBanned)
            {
                r.Reject("invited_account_inactive", nowUtc);
                rejected++;
                continue;
            }

            if (referrer is null || referrer.IsDeleted || referrer.IsBanned)
            {
                r.Reject("referrer_inactive", nowUtc);
                rejected++;
                continue;
            }

            // The same phone number on both accounts: one person inviting themselves. The unique index on the
            // lookup hash already stops two accounts holding it at once, so this is a guard in case that index changes.
            if (referred.PhoneNumberLookupHash is not null && referred.PhoneNumberLookupHash == referrer.PhoneNumberLookupHash)
            {
                r.Reject("same_phone", nowUtc);
                rejected++;
                continue;
            }

            var phoneOk = !options.RequireConfirmedPhone || referred.PhoneNumberConfirmed;
            if (phoneOk && withListing.Contains(r.ReferredUserId))
            {
                var count = qualifiedRecently.GetValueOrDefault(r.ReferrerUserId);
                if (count >= options.MaxQualifiedPerReferrerPer30Days)
                {
                    r.Reject("referrer_cap", nowUtc);
                    rejected++;
                    continue;
                }

                r.Qualify(nowUtc);
                qualifiedRecently[r.ReferrerUserId] = count + 1;
                newlyQualified.Add(r);
                qualified++;
                continue;
            }

            if (r.CreatedAt <= nowUtc.AddDays(-options.WindowDays))
            {
                r.Expire(nowUtc);
                expired++;
            }
        }

        await db.SaveChangesAsync(ct);

        // After the save: a failed notification must not undo a decision.
        foreach (var r in newlyQualified)
        {
            await notifications.NotifyReferralQualifiedAsync(r.ReferrerUserId, r.Id, ct);
            await growth.RecordAsync(GrowthEventNames.ReferralQualified, $"referral_qualified:{r.Id:N}",
                r.ReferrerUserId, subjectId: r.Id, ct: ct);
        }

        return new SweepResult(qualified, rejected, expired);
    }
}
