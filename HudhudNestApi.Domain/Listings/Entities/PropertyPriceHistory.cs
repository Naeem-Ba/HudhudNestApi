using HudhudNestApi.Domain.Common.Entities;

namespace HudhudNestApi.Domain.Listings.Entities;

/// <summary>
/// Append-only record of a single price field change on a Property.
/// Phase-0 "freshness" feature: gives buyers/renters a "last price change" signal
/// and gives the platform a real price-history timeline per listing. Never updated
/// or soft-deleted after creation — each edit that changes a price field creates a
/// new row (see UpdatePropertyCommandHandler.NotifyChangesAsync, which is where
/// these rows are written, in the same SaveChanges as the property update itself).
/// </summary>
public class PropertyPriceHistory : BaseEntity
{
    public Guid PropertyId { get; private set; }
    public Property? Property { get; private set; }

    /// <summary>Which price field changed — e.g. "ColdRent", "WarmRent", "PurchasePrice".</summary>
    public string PriceField { get; private set; } = string.Empty;

    public decimal? OldValue { get; private set; }
    public decimal? NewValue { get; private set; }

    /// <summary>ISO 4217 currency code the values are denominated in.</summary>
    public string CurrencyCode { get; private set; } = string.Empty;

    public Guid ChangedByUserId { get; private set; }
    public DateTime ChangedAt { get; private set; }

    private PropertyPriceHistory() { }

    public static PropertyPriceHistory Create(
        Guid propertyId,
        string priceField,
        decimal? oldValue,
        decimal? newValue,
        string currencyCode,
        Guid changedByUserId)
    {
        return new PropertyPriceHistory
        {
            PropertyId = propertyId,
            PriceField = priceField,
            OldValue = oldValue,
            NewValue = newValue,
            CurrencyCode = currencyCode,
            ChangedByUserId = changedByUserId,
            ChangedAt = DateTime.UtcNow
        };
    }
}
