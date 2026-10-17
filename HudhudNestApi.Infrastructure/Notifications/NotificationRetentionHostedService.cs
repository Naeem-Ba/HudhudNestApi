using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Notifications;

/// <summary>
/// Retention for the notification log, so the table does not grow without bound:
/// read notifications are removed after <c>Notifications:Retention:ReadDays</c> (default 180) and the ones
/// a user removed after <c>Notifications:Retention:DeletedDays</c> (default 30). Unread notifications are
/// never removed by age. Postgres advisory lock: one instance sweeps at a time.
/// </summary>
public sealed class NotificationRetentionHostedService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);
    private const int BatchSize = 500;
    private const int MaxBatchesPerSweep = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<NotificationRetentionHostedService> _logger;

    public NotificationRetentionHostedService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<NotificationRetentionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the host finish starting (and migrations run) before the first sweep.
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Notification retention sweep failed.");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal async Task<int> RunOnceAsync(CancellationToken ct, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        var readDays = Math.Max(30, _configuration.GetValue("Notifications:Retention:ReadDays", 180));
        var deletedDays = Math.Max(1, _configuration.GetValue("Notifications:Retention:DeletedDays", 30));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

        var removed = 0;
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await BackgroundJobLock.TryRunAsync(
                db.Database.GetDbConnection(),
                BackgroundJobLockKeys.NotificationRetention,
                nameof(NotificationRetentionHostedService),
                _logger,
                async () =>
                {
                    for (var i = 0; i < MaxBatchesPerSweep; i++)
                    {
                        var ids = await repository.DeleteExpiredAsync(
                            now.AddDays(-readDays), now.AddDays(-deletedDays), BatchSize, ct);
                        removed += ids.Count;
                        if (ids.Count < BatchSize)
                        {
                            break;
                        }
                    }
                },
                ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        if (removed > 0)
        {
            _logger.LogInformation("Notification retention removed {Count} rows.", removed);
        }

        return removed;
    }
}
