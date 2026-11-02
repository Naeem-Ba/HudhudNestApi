using HudhudNestApi.Application.Listings.Map;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HudhudNestApi.Infrastructure.Listings;

/// <summary>
/// Growth engine P8. One count, then either up to 300 points or a GROUP BY over grid cells, all in SQL. A
/// listing whose owner hid the exact spot is placed at its <see cref="LocationPrivacy"/> cell centre before
/// anything else, so neither a point nor a cluster centre carries its exact position.
/// </summary>
public sealed class PropertyMapReader : IPropertyMapReader
{
    private const decimal Cell = LocationPrivacy.CellDegrees;
    private const decimal HalfCell = LocationPrivacy.CellDegrees / 2m;

    private readonly AppDbContext _db;

    public PropertyMapReader(AppDbContext db) => _db = db;

    public async Task<MapResultDto> GetAsync(GetMapListingsQuery q, DateTime nowUtc, CancellationToken ct)
    {
        var listings = _db.Properties.AsNoTracking()
            .Where(Property.OnMarket(nowUtc))
            .Where(p => p.Latitude != null && p.Longitude != null);
        if (q.ListingType is { } type)
            listings = type == ListingType.ForRentAndSale
                ? listings.Where(p => p.ListingType == ListingType.ForRentAndSale)
                : listings.Where(p => p.ListingType == type || p.ListingType == ListingType.ForRentAndSale);
        if (q.PropertyTypeId is { } propertyType)
            listings = listings.Where(p => p.PropertyTypeId == propertyType);
        if (q.MinRooms is { } rooms)
            listings = listings.Where(p => p.Rooms >= rooms);

        // The point the public may see, computed in SQL.
        var shown = listings.Select(p => new
        {
            p.Id,
            p.Title,
            Lat = p.HideExactLocation ? Math.Floor(p.Latitude!.Value / Cell) * Cell + HalfCell : p.Latitude!.Value,
            Lng = p.HideExactLocation ? Math.Floor(p.Longitude!.Value / Cell) * Cell + HalfCell : p.Longitude!.Value,
            p.HideExactLocation,
            p.ListingType,
            p.PurchasePrice,
            Rent = p.ColdRent ?? p.WarmRent,
            p.CurrencyCode,
            p.Rooms,
            p.Area,
            MainImageUrl = p.Images.Where(i => i.IsMain).Select(i => i.Url).FirstOrDefault(),
            IsFeatured = p.IsFeatured && p.FeaturedUntil != null && p.FeaturedUntil > nowUtc,
        })
        .Where(x => x.Lat >= q.MinLat && x.Lat <= q.MaxLat && x.Lng >= q.MinLng && x.Lng <= q.MaxLng);

        var total = await shown.CountAsync(ct);
        if (total <= GetMapListingsQuery.MaxPoints)
        {
            var points = await shown
                .OrderByDescending(x => x.IsFeatured).ThenBy(x => x.Id)
                .Take(GetMapListingsQuery.MaxPoints)
                .ToListAsync(ct);
            return new MapResultDto("points", total,
                points.Select(x => new MapPointDto(
                    x.Id, x.Title, decimal.Round(x.Lat, 6), decimal.Round(x.Lng, 6), x.HideExactLocation,
                    x.ListingType.ToString(), x.PurchasePrice, x.Rent, x.CurrencyCode.Trim(), x.Rooms, x.Area,
                    x.MainImageUrl, x.IsFeatured)).ToList(),
                []);
        }

        var size = GetMapListingsQuery.CellDegrees(q.Zoom);
        var clusters = await shown
            .GroupBy(x => new { Row = Math.Floor(x.Lat / size), Col = Math.Floor(x.Lng / size) })
            .Select(g => new { Lat = g.Average(x => x.Lat), Lng = g.Average(x => x.Lng), Count = g.Count() })
            .ToListAsync(ct);
        return new MapResultDto("clusters", total, [],
            clusters.Select(c => new MapClusterDto(decimal.Round(c.Lat, 5), decimal.Round(c.Lng, 5), c.Count))
                .OrderByDescending(c => c.Count).ToList());
    }
}
