using MediatR;

namespace HudhudNestApi.Application.Listings.Queries.GetPropertyPriceHistory;

/// <summary>
/// Public price timeline of one listing (growth engine P1). Null result means the listing is not
/// publicly viewable, which the controller answers with 404 so drafts and taken-down listings stay hidden.
/// </summary>
public sealed record GetPropertyPriceHistoryQuery(Guid PropertyId) : IRequest<PropertyPriceHistoryDto?>;

/// <summary>
/// One change of one price field. Who made the change is never exposed. <see cref="ChangePercent"/> is
/// only set when both values are in the same currency and the old value is positive.
/// </summary>
public sealed record PropertyPriceChangeDto(
    string PriceField,
    decimal? OldValue,
    decimal? NewValue,
    string CurrencyCode,
    string? OldCurrencyCode,
    decimal? ChangePercent,
    DateTime ChangedAt);

public sealed record PropertyPriceHistoryDto(
    Guid PropertyId,
    IReadOnlyList<PropertyPriceChangeDto> Changes);
