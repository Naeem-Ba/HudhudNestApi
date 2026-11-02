using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Domain.Listings.Entities;
using MediatR;

namespace HudhudNestApi.Application.Listings.Queries.GetPropertyPriceHistory;

public sealed class GetPropertyPriceHistoryQueryHandler
    : IRequestHandler<GetPropertyPriceHistoryQuery, PropertyPriceHistoryDto?>
{
    /// <summary>Enough for any real listing; bounds the response if an owner edits the price in a loop.</summary>
    public const int MaxEntries = 50;

    private readonly IPropertyRepository _properties;
    private readonly IPropertyPriceHistoryRepository _history;

    public GetPropertyPriceHistoryQueryHandler(IPropertyRepository properties, IPropertyPriceHistoryRepository history)
    {
        _properties = properties;
        _history = history;
    }

    public async Task<PropertyPriceHistoryDto?> Handle(GetPropertyPriceHistoryQuery request, CancellationToken cancellationToken)
    {
        if (!await _properties.IsPubliclyVisibleAsync(request.PropertyId, cancellationToken))
            return null;

        var rows = await _history.GetLatestAsync(request.PropertyId, MaxEntries, cancellationToken);

        return new PropertyPriceHistoryDto(request.PropertyId, rows.Select(ToDto).ToList());
    }

    public static PropertyPriceChangeDto ToDto(PropertyPriceHistory row) => new(
        row.PriceField,
        row.OldValue,
        row.NewValue,
        row.CurrencyCode.Trim(),
        row.OldCurrencyCode?.Trim(),
        ChangePercentOf(row),
        row.ChangedAt);

    public static decimal? ChangePercentOf(PropertyPriceHistory row)
        => row.IsComparable
            ? Math.Round((row.NewValue!.Value - row.OldValue!.Value) / row.OldValue.Value * 100m, 1, MidpointRounding.AwayFromZero)
            : null;
}
