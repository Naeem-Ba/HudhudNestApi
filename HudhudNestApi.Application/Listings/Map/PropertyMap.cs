using FluentValidation;
using HudhudNestApi.Domain.Enums;
using MediatR;

namespace HudhudNestApi.Application.Listings.Map;

public sealed record MapPointDto(
    Guid Id, string Title, decimal Latitude, decimal Longitude, bool IsLocationApproximate,
    string ListingType, decimal? PurchasePrice, decimal? Rent, string CurrencyCode, int? Rooms, decimal? Area,
    string? MainImageUrl, bool IsFeatured);

public sealed record MapClusterDto(decimal Latitude, decimal Longitude, int Count);

/// <summary>"points" when the box holds at most <see cref="GetMapListingsQuery.MaxPoints"/> listings, else "clusters".</summary>
public sealed record MapResultDto(string Mode, int Total, IReadOnlyList<MapPointDto> Points, IReadOnlyList<MapClusterDto> Clusters);

/// <summary>
/// Growth engine P8: listings on the market inside a map box. Few listings: one point each (hidden spots are
/// approximate). Many: server-side grid clusters sized by zoom, so the browser never receives thousands of
/// points and cluster centres are built from the already-approximated points.
/// </summary>
public sealed record GetMapListingsQuery(
    decimal MinLat, decimal MinLng, decimal MaxLat, decimal MaxLng, int Zoom,
    ListingType? ListingType, int? PropertyTypeId, int? MinRooms) : IRequest<MapResultDto>
{
    public const int MaxPoints = 300;

    /// <summary>Grid cell in degrees: a quarter of a 256-px map tile at this zoom (about 64 px on screen).</summary>
    public static decimal CellDegrees(int zoom) => 360m / (decimal)Math.Pow(2, Math.Clamp(zoom, 1, 20)) / 4m;
}

public interface IPropertyMapReader
{
    Task<MapResultDto> GetAsync(GetMapListingsQuery query, DateTime nowUtc, CancellationToken ct);
}

public sealed class GetMapListingsQueryValidator : AbstractValidator<GetMapListingsQuery>
{
    public GetMapListingsQueryValidator()
    {
        RuleFor(x => x.MinLat).InclusiveBetween(-90, 90);
        RuleFor(x => x.MaxLat).InclusiveBetween(-90, 90).GreaterThan(x => x.MinLat);
        RuleFor(x => x.MinLng).InclusiveBetween(-180, 180);
        RuleFor(x => x.MaxLng).InclusiveBetween(-180, 180).GreaterThan(x => x.MinLng);
        RuleFor(x => x.Zoom).InclusiveBetween(1, 20);
        RuleFor(x => x.ListingType).IsInEnum().When(x => x.ListingType.HasValue);
        RuleFor(x => x.PropertyTypeId).GreaterThan(0).When(x => x.PropertyTypeId.HasValue);
        RuleFor(x => x.MinRooms).InclusiveBetween(0, 50).When(x => x.MinRooms.HasValue);
    }
}

public sealed class GetMapListingsQueryHandler : IRequestHandler<GetMapListingsQuery, MapResultDto>
{
    private readonly IPropertyMapReader _reader;
    private readonly TimeProvider _time;

    public GetMapListingsQueryHandler(IPropertyMapReader reader, TimeProvider? time = null)
    {
        _reader = reader;
        _time = time ?? TimeProvider.System;
    }

    public Task<MapResultDto> Handle(GetMapListingsQuery q, CancellationToken ct) =>
        _reader.GetAsync(q, _time.GetUtcNow().UtcDateTime, ct);
}
