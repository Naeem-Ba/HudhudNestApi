using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Plans.Services;
using HudhudNestApi.Domain.Plans.Constants;
using HudhudNestApi.Domain.Users.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Plans;

/// <summary>
/// QA Phase 13 (P13-D03): a time-limited plan (an administrator grants it for N days) used to lapse without a word, and the
/// owner found out when listing creation was refused. This tells them five days before and on the day it ends.
///
/// Nothing here changes the plan: expiry is already computed on read (<c>UserAccount.GetEffectivePlanStatus</c>) and the
/// listing quota falls back to the free tier by itself. The notification service ignores a repeat of the same dedupe key,
/// so a sweep that runs again, or on another instance, sends nothing twice; a renewal moves the end date and earns a fresh
/// warning. "Ended" notices only look back a few days, so the first sweep after deployment does not announce plans that
/// lapsed months ago.
/// </summary>
public sealed class PlanExpiryNoticeHostedService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan WarningLeadTime = TimeSpan.FromDays(5);
    private static readonly TimeSpan EndedLookBack = TimeSpan.FromDays(7);
    private const int MaxItemsPerPhase = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlanExpiryNoticeHostedService> _logger;

    public PlanExpiryNoticeHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<PlanExpiryNoticeHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Plan expiry notice service started. Interval={Interval}, WarningLeadTime={Lead}.",
            SweepInterval, WarningLeadTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plan expiry notice sweep failed.");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }

    /// <summary>One sweep, internal so a test can drive it without the timer. Guarded by an advisory lock like the other jobs.</summary>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var billing = scope.ServiceProvider.GetRequiredService<BillingNotifier>();

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await BackgroundJobLock.TryRunAsync(
                db.Database.GetDbConnection(),
                BackgroundJobLockKeys.PlanExpiryNotice,
                nameof(PlanExpiryNoticeHostedService),
                _logger,
                async () =>
                {
                    var now = DateTime.UtcNow;

                    var soon = await (
                        from account in db.UserAccounts.AsNoTracking()
                        join plan in db.Plans.AsNoTracking() on account.PlanId equals plan.Id
                        where account.PlanStatus == PlanStatus.Active
                              && plan.Tier != PlanTiers.Free
                              && account.PlanExpiresAt != null
                              && account.PlanExpiresAt > now
                              && account.PlanExpiresAt <= now + WarningLeadTime
                        orderby account.PlanExpiresAt
                        select new { account.Id, plan.Tier, ExpiresAt = account.PlanExpiresAt!.Value })
                        .Take(MaxItemsPerPhase)
                        .ToListAsync(ct);

                    foreach (var row in soon)
                    {
                        await billing.PlanExpiringSoonAsync(row.Id, row.Tier, row.ExpiresAt, ct);
                    }

                    var ended = await (
                        from account in db.UserAccounts.AsNoTracking()
                        join plan in db.Plans.AsNoTracking() on account.PlanId equals plan.Id
                        where account.PlanStatus == PlanStatus.Active
                              && plan.Tier != PlanTiers.Free
                              && account.PlanExpiresAt != null
                              && account.PlanExpiresAt <= now
                              && account.PlanExpiresAt > now - EndedLookBack
                        orderby account.PlanExpiresAt
                        select new { account.Id, plan.Tier, ExpiresAt = account.PlanExpiresAt!.Value })
                        .Take(MaxItemsPerPhase)
                        .ToListAsync(ct);

                    foreach (var row in ended)
                    {
                        await billing.PlanExpiredAsync(row.Id, row.Tier, row.ExpiresAt, ct);
                    }

                    if (soon.Count + ended.Count > 0)
                    {
                        _logger.LogInformation(
                            "Plan expiry notice sweep complete. Warned={Warned}, Ended={Ended}.", soon.Count, ended.Count);
                    }
                },
                ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
