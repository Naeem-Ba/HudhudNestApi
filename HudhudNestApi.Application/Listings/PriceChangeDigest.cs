using System.Globalization;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Listings;

/// <summary>
/// Growth engine P1: turns the not-yet-announced price-history rows of one listing into at most one
/// follower notification. Several edits in a row collapse into their net effect (first old value →
/// latest new value per field); a change that was undone again announces nothing.
/// </summary>
public static class PriceChangeDigest
{
    /// <summary>The field followers care about most comes first.</summary>
    private static readonly string[] FieldOrder = ["PurchasePrice", "ColdRent", "WarmRent"];

    public sealed record Summary(Guid AnnouncedRowId, NotificationText Detail);

    public static Summary? Summarize(IEnumerable<PropertyPriceHistory> pendingRows)
    {
        var byField = pendingRows
            .GroupBy(row => row.PriceField, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(r => r.ChangedAt).ThenBy(r => r.Id).ToList(), StringComparer.Ordinal);

        foreach (var field in FieldOrder.Concat(byField.Keys.Where(k => !FieldOrder.Contains(k)).OrderBy(k => k, StringComparer.Ordinal)))
        {
            if (!byField.TryGetValue(field, out var rows) || rows.Count == 0)
                continue;

            var first = rows[0];
            var last = rows[^1];
            var oldCurrency = (first.OldCurrencyCode ?? first.CurrencyCode).Trim();
            var newCurrency = last.CurrencyCode.Trim();

            var unchanged = first.OldValue == last.NewValue &&
                string.Equals(oldCurrency, newCurrency, StringComparison.OrdinalIgnoreCase);
            if (unchanged || !last.NewValue.HasValue)
                continue; // undone, or the price was removed (nothing useful to tell a follower)

            var key = field switch
            {
                "PurchasePrice" => NotificationKeys.PurchasePriceChangedDetail,
                "ColdRent" => NotificationKeys.ColdRentChangedDetail,
                "WarmRent" => NotificationKeys.WarmRentChangedDetail,
                _ => null,
            };
            if (key is null)
                continue;

            return new Summary(
                last.Id,
                NotificationText.Of(key, ("from", Money(first.OldValue, oldCurrency)), ("to", Money(last.NewValue, newCurrency))));
        }

        return null;
    }

    private static string Money(decimal? value, string currency)
        => value.HasValue
            ? $"{value.Value.ToString("0.##", CultureInfo.InvariantCulture)} {currency}".Trim()
            : "-";
}
