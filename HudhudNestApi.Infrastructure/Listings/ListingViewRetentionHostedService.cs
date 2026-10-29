using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Listings.Views;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Listings;

/// <summary>
/// Removes view-ledger rows older than <c>ListingViews:RetentionDays</c> (default 90) so the table
/// stays bounded. The listing totals are columns on the listings and are not touched, so the public
/// counter never goes down; only the windowed/daily history is limited to the retention window.
/// Postgres advisory lock: one instance sweeps at a time.
/// </summary>
public sealed class ListingViewRetentionHostedService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);
    private const int BatchSize = 5000;
    private const int MaxBatchesPerSweep = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ListingViewRetentionHostedService> _logger;

    public ListingViewRetentionHostedService(
        IServiceScopeFactory scopeFactory, ILogger<ListingViewRetentionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken); }
        catch (OperationCanceledException) { return; }

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
                _logger.LogError(ex, "Listing view retention sweep failed.");
            }

            try { await Task.Delay(SweepInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    internal async Task<int> RunOnceAsync(CancellationToken ct, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var views = scope.ServiceProvider.GetRequiredService<IListingViewRepository>();
        var policy = scope.ServiceProvider.GetRequiredService<IListingViewPolicy>();

        var removed = 0;
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await BackgroundJobLock.TryRunAsync(
                db.Database.GetDbConnection(),
                BackgroundJobLockKeys.ListingViewRetention,
                nameof(ListingViewRetentionHostedService),
                _logger,
                async () =>
                {
                    for (var i = 0; i < MaxBatchesPerSweep; i++)
                    {
                        var n = await views.PurgeOlderThanAsync(now.AddDays(-policy.RetentionDays), BatchSize, ct);
                        removed += n;
                        if (n < BatchSize)
                            break;
                    }
                },
                ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        if (removed > 0)
            _logger.LogInformation("Listing view retention removed {Count} rows.", removed);

        return removed;
    }
}
