using Microsoft.EntityFrameworkCore;
using HudhudNestApi.Application.Analytics.DTOs;
using HudhudNestApi.Application.Analytics.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Domain.Bookings.Enums;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Analytics;

public sealed class AnalyticsReadRepository : IAnalyticsReadRepository
{
    private readonly AppDbContext _db;

    public AnalyticsReadRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PropertyStatsDto> GetPropertyStatsAsync(
        Guid propertyId,
        Guid ownerId,
        CancellationToken ct = default)
    {
        var property = await _db.Properties
            .Where(p => p.Id == propertyId)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.OwnerId,
                p.City,
                p.Area
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Property was not found.");

        if (property.OwnerId != ownerId)
        {
            throw new ForbiddenException("Property statistics are available only to the property owner.");
        }

        var visitStats = await _db.VisitRequests
            .Where(v => v.PropertyId == propertyId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Confirmed = g.Count(v => v.Status == VisitStatus.Confirmed)
            })
            .FirstOrDefaultAsync(ct);

        var totalMessages = await _db.Messages
            .CountAsync(m => m.PropertyId == propertyId, ct);

        var totalFavorites = await _db.Favorites
            .CountAsync(f => f.PropertyId == propertyId, ct);

        var reviewStats = await _db.PropertyReviews
            .Where(r => r.PropertyId == propertyId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Average = g.Average(r => (double)r.Rating)
            })
            .FirstOrDefaultAsync(ct);

        decimal? cityAveragePricePerSquareMeter = await _db.Properties
            .Where(p =>
                p.City == property.City &&
                p.Id != property.Id &&
                p.IsPublished &&
                p.Area.HasValue &&
                p.Area.Value > 0 &&
                p.ColdRent.HasValue)
            .AverageAsync(p => (decimal?)(p.ColdRent!.Value / p.Area!.Value), ct);

        return new PropertyStatsDto(
            PropertyId: property.Id,
            PropertyTitle: property.Title,
            TotalVisitRequests: visitStats?.Total ?? 0,
            ConfirmedVisits: visitStats?.Confirmed ?? 0,
            TotalMessages: totalMessages,
            TotalFavorites: totalFavorites,
            TotalReviews: reviewStats?.Count ?? 0,
            AverageRating: Math.Round(reviewStats?.Average ?? 0, 1),
            AreaAveragePrice: cityAveragePricePerSquareMeter.HasValue
                ? Math.Round(cityAveragePricePerSquareMeter.Value, 2)
                : null);
    }

    public async Task<MarketInsightsDto> GetMarketInsightsAsync(
        string? countryCode,
        CancellationToken ct = default)
    {
        var query = _db.Properties
            .Where(p => p.IsPublished && !p.IsDeleted);

        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            var normalizedCountryCode = countryCode.Trim().ToUpperInvariant();
            query = query.Where(p => p.CountryCode == normalizedCountryCode);
        }

        var insights = await query
            .GroupBy(p => new { p.City, p.CountryCode })
            .Select(g => new CityMarketInsightDto(
                g.Key.City,
                g.Key.CountryCode,
                (decimal)(g
                    .Where(p => p.ListingType == ListingType.ForRent && p.ColdRent.HasValue)
                    .Average(p => (double?)p.ColdRent) ?? 0),
                (decimal)(g
                    .Where(p => p.ListingType == ListingType.ForSale && p.PurchasePrice.HasValue)
                    .Average(p => (double?)p.PurchasePrice) ?? 0),
                g.Count()))
            .OrderByDescending(c => c.TotalActiveListings)
            .Take(20)
            .ToListAsync(ct);

        return new MarketInsightsDto(
            Cities: insights.AsReadOnly(),
            GeneratedAt: DateTime.UtcNow);
    }
}
