using HudhudNestApi.Application.Pricing;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Pricing;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HudhudNestApi.Infrastructure.Pricing;

/// <summary>
/// Growth engine P6: rebuilds AreaPriceStats from live listings every few hours, under an advisory lock, in one
/// transaction (readers see the old table or the new one, never half). Work is a single projection of live
/// listings with a size and a price, then in-memory grouping: fine for tens of thousands of listings.
/// </summary>
public sealed class AreaPriceStatsHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AreaPriceStatsHostedService> _logger;

    public AreaPriceStatsHostedService(IServiceScopeFactory scopeFactory, ILogger<AreaPriceStatsHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the app finish starting before the first rebuild.
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            var hours = 6;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                hours = Math.Max(1, scope.ServiceProvider.GetRequiredService<IOptions<AreaPriceOptions>>().Value.RecomputeHours);
                await RunOnceAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Area price statistics rebuild failed.");
            }

            await Task.Delay(TimeSpan.FromHours(hours), stoppingToken);
        }
    }

    internal async Task<int> RunOnceAsync(DateTime nowUtc, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<AreaPriceOptions>>().Value;
        var written = 0;

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await BackgroundJobLock.TryRunAsync(
                db.Database.GetDbConnection(),
                BackgroundJobLockKeys.AreaPriceStats,
                nameof(AreaPriceStatsHostedService),
                _logger,
                async () => written = await RebuildAsync(db, options, nowUtc, ct),
                ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return written;
    }

    internal static IReadOnlyList<AreaPriceStat> Build(
        IEnumerable<(int GovernorateId, int? DistrictId, int? PropertyTypeId, PriceKind Kind, PriceSample Sample)> rows,
        AreaPriceOptions options, DateTime nowUtc)
    {
        var groups = new Dictionary<(int, int?, int?, PriceKind), List<PriceSample>>();
        void Add((int, int?, int?, PriceKind) key, PriceSample s)
        {
            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = [];
            list.Add(s);
        }

        foreach (var r in rows)
        {
            Add((r.GovernorateId, null, null, r.Kind), r.Sample);
            if (r.PropertyTypeId.HasValue)
                Add((r.GovernorateId, null, r.PropertyTypeId, r.Kind), r.Sample);
            if (r.DistrictId.HasValue)
            {
                Add((r.GovernorateId, r.DistrictId, null, r.Kind), r.Sample);
                if (r.PropertyTypeId.HasValue)
                    Add((r.GovernorateId, r.DistrictId, r.PropertyTypeId, r.Kind), r.Sample);
            }
        }

        var result = new List<AreaPriceStat>();
        foreach (var ((gov, district, type, kind), samples) in groups)
        {
            var summary = AreaPriceStatistics.Summarize(samples, options);
            if (summary is null)
                continue;
            result.Add(AreaPriceStat.Create(gov, district, type, kind, summary.SampleCount, summary.ExcludedCount,
                summary.P25, summary.Median, summary.P75, summary.Confidence, summary.Oldest, summary.Newest, nowUtc));
        }

        return result;
    }

    private static async Task<int> RebuildAsync(AppDbContext db, AreaPriceOptions options, DateTime nowUtc, CancellationToken ct)
    {
        var rates = await PropertyRepository.LoadCurrencyRatesAsync(db, ct);
        var listings = await db.Properties.AsNoTracking()
            .Where(Property.OnMarket(nowUtc))
            .Where(p => p.GovernorateId != null && p.Area != null && p.Area > 0)
            .Select(p => new
            {
                p.OwnerId,
                GovernorateId = p.GovernorateId!.Value,
                p.DistrictId,
                p.PropertyTypeId,
                p.ListingType,
                Area = p.Area!.Value,
                p.PurchasePrice,
                Rent = p.ColdRent ?? p.WarmRent,
                Currency = p.CurrencyCode.Trim(),
                PublishedAt = p.PublishedAt ?? p.CreatedAt,
            })
            .ToListAsync(ct);

        var rows = new List<(int, int?, int?, PriceKind, PriceSample)>();
        foreach (var l in listings)
        {
            void Add(decimal? price, PriceKind kind)
            {
                if (price is not > 0)
                    return;
                decimal? usd = l.Currency == "USD" ? price.Value
                    : rates.TryGetRate(l.Currency, out var unitsPerUsd) ? price.Value / unitsPerUsd : null;
                if (usd is not > 0)
                    return; // no rate for this currency: left out rather than guessed
                rows.Add((l.GovernorateId, l.DistrictId, l.PropertyTypeId, kind,
                    new PriceSample(l.OwnerId, l.DistrictId, l.Area, usd.Value, l.PublishedAt)));
            }

            if (l.ListingType is ListingType.ForSale or ListingType.ForRentAndSale)
                Add(l.PurchasePrice, PriceKind.Sale);
            if (l.ListingType is ListingType.ForRent or ListingType.ForRentAndSale)
                Add(l.Rent, PriceKind.Rent);
        }

        var stats = Build(rows, options, nowUtc);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.AreaPriceStats.ExecuteDeleteAsync(ct);
        db.AreaPriceStats.AddRange(stats);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return stats.Count;
    }
}
