using HudhudNestApi.Domain.Common.Entities;

namespace HudhudNestApi.Domain.Listings.Entities;

/// <summary>
/// Append-only record of a single price field change on a Property.
/// Phase-0 "freshness" feature: gives buyers/renters a "last price change" signal
/// and gives the platform a real price-history timeline per listing. The price facts are
/// never updated or soft-deleted after creation (only the follower-notification stamp is
/// set once) — each edit that changes a price field of a published listing creates a
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

    /// <summary>ISO 4217 currency code <see cref="NewValue"/> is denominated in.</summary>
    public string CurrencyCode { get; private set; } = string.Empty;

    /// <summary>
    /// Currency of <see cref="OldValue"/> when the owner switched currency in the same edit; null when
    /// both values share <see cref="CurrencyCode"/>. Rows written before this column existed are null too.
    /// </summary>
    public string? OldCurrencyCode { get; private set; }

    /// <summary>
    /// When the users who saved this listing were told about this change (growth engine P1). Null means
    /// not yet handled; PriceChangeFollowerNotificationHostedService sets it after its sweep.
    /// </summary>
    public DateTime? FollowersNotifiedAt { get; private set; }

    public Guid ChangedByUserId { get; private set; }
    public DateTime ChangedAt { get; private set; }

    private PropertyPriceHistory() { }

    public static PropertyPriceHistory Create(
        Guid propertyId,
        string priceField,
        decimal? oldValue,
        decimal? newValue,
        string currencyCode,
        Guid changedByUserId,
        string? oldCurrencyCode = null)
    {
        var normalizedOldCurrency = string.IsNullOrWhiteSpace(oldCurrencyCode) ||
            string.Equals(oldCurrencyCode, currencyCode, StringComparison.OrdinalIgnoreCase)
                ? null
                : oldCurrencyCode.Trim().ToUpperInvariant();

        return new PropertyPriceHistory
        {
            PropertyId = propertyId,
            PriceField = priceField,
            OldValue = oldValue,
            NewValue = newValue,
            CurrencyCode = currencyCode,
            OldCurrencyCode = normalizedOldCurrency,
            ChangedByUserId = changedByUserId,
            ChangedAt = DateTime.UtcNow
        };
    }

    /// <summary>Same currency on both sides, so a percent change means something.</summary>
    public bool IsComparable => OldCurrencyCode is null && OldValue is > 0 && NewValue.HasValue;

    public void MarkFollowersNotified(DateTime atUtc)
    {
        FollowersNotifiedAt ??= atUtc;
    }
}
