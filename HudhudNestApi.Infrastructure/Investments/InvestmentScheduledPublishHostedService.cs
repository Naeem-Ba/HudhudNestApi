using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Investments.Commands.PublishInvestmentProject;
using HudhudNestApi.Domain.Investments.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Investments;

/// <summary>
/// QA Phase 10, P10-D03: publishes Scheduled investment projects whose publish time has come. Until now
/// <c>ScheduledPublishAt</c> was stored and never acted on, and the manual publish worked before it.
///
/// Each project goes through the same <see cref="PublishInvestmentProjectCommand"/> as a manual publish, so the
/// readiness rules (financials, risk, required public documents) are checked at the moment of publication; a
/// project that is no longer ready stays Scheduled, is logged and is retried on the next sweep (an admin fixes the
/// gap, or reschedules). Idempotent and derived from timestamps in the database, so a missed sweep (the free
/// Render instance sleeps) only delays it. One Postgres advisory lock keeps two instances from sweeping together.
/// </summary>
public sealed class InvestmentScheduledPublishHostedService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    private const int MaxItemsPerSweep = 50;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InvestmentScheduledPublishHostedService> _logger;

    public InvestmentScheduledPublishHostedService(
        IServiceScopeFactory scopeFactory, ILogger<InvestmentScheduledPublishHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Investment scheduled-publish service started. Interval={Interval}.", SweepInterval);

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
                _logger.LogError(ex, "Investment scheduled-publish sweep failed.");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }

    /// <summary>One full sweep. Internal so a test can drive it without waiting on the timer.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken ct, DateTime? utcNow = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var published = 0;

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await BackgroundJobLock.TryRunAsync(
                db.Database.GetDbConnection(),
                BackgroundJobLockKeys.InvestmentScheduledPublish,
                nameof(InvestmentScheduledPublishHostedService),
                _logger,
                async () =>
                {
                    published = await PublishDueAsync(scope.ServiceProvider, db, utcNow ?? DateTime.UtcNow, ct);
                    if (published > 0)
                        _logger.LogInformation("Investment scheduled-publish sweep complete. Published={Published}.", published);
                },
                ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return published;
    }

    private async Task<int> PublishDueAsync(IServiceProvider services, AppDbContext db, DateTime now, CancellationToken ct)
    {
        var dueIds = await db.InvestmentProjects
            .AsNoTracking()
            .Where(p => p.Status == InvestmentProjectStatus.Scheduled
                        && p.ScheduledPublishAt != null && p.ScheduledPublishAt <= now)
            .OrderBy(p => p.ScheduledPublishAt)
            .Select(p => p.Id)
            .Take(MaxItemsPerSweep)
            .ToListAsync(ct);

        var published = 0;
        var sender = services.GetRequiredService<ISender>();
        foreach (var id in dueIds)
        {
            try
            {
                await sender.Send(new PublishInvestmentProjectCommand(id), ct);
                published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Not ready (a readiness rule fails) or already handled elsewhere: stays Scheduled, retried next sweep.
                _logger.LogWarning(ex,
                    "Scheduled investment project {ProjectId} could not be published yet; it is retried on the next sweep.", id);
                db.ChangeTracker.Clear();
            }
        }

        return published;
    }
}
