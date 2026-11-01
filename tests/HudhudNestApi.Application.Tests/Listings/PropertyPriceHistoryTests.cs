using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Tests.Listings;

public sealed class PropertyPriceHistoryTests
{
    [Fact]
    public void Create_CapturesOldAndNewValues()
    {
        var propertyId = Guid.NewGuid();
        var changedBy = Guid.NewGuid();

        var entry = PropertyPriceHistory.Create(
            propertyId,
            priceField: "ColdRent",
            oldValue: 100m,
            newValue: 120m,
            currencyCode: "USD",
            changedByUserId: changedBy);

        Assert.Equal(propertyId, entry.PropertyId);
        Assert.Equal("ColdRent", entry.PriceField);
        Assert.Equal(100m, entry.OldValue);
        Assert.Equal(120m, entry.NewValue);
        Assert.Equal("USD", entry.CurrencyCode);
        Assert.Equal(changedBy, entry.ChangedByUserId);
    }
}

public sealed class PropertyAvailabilityConfirmationTests
{
    [Fact]
    public void ConfirmStillAvailable_SetsLastConfirmedAvailableAt()
    {
        var property = Property.Create(
            title: "Test apartment",
            description: "Availability confirmation domain test",
            ownerId: Guid.NewGuid(),
            listingType: ListingType.ForRent,
            countryCode: "SY",
            currencyCode: "SYP");

        Assert.Null(property.LastConfirmedAvailableAt);

        property.ConfirmStillAvailable();

        Assert.NotNull(property.LastConfirmedAvailableAt);
    }
}

public sealed class PropertyPriceHistoryCurrencyTests
{
    [Fact]
    public void Same_old_currency_is_stored_as_null()
    {
        var entry = PropertyPriceHistory.Create(Guid.NewGuid(), "PurchasePrice", 1m, 2m, "USD", Guid.NewGuid(), oldCurrencyCode: "usd");

        Assert.Null(entry.OldCurrencyCode);
        Assert.True(entry.IsComparable);
    }

    [Fact]
    public void A_missing_old_value_is_not_comparable()
    {
        var entry = PropertyPriceHistory.Create(Guid.NewGuid(), "ColdRent", null, 2m, "USD", Guid.NewGuid());

        Assert.False(entry.IsComparable);
    }

    [Fact]
    public void Followers_stamp_is_set_once()
    {
        var entry = PropertyPriceHistory.Create(Guid.NewGuid(), "ColdRent", 1m, 2m, "USD", Guid.NewGuid());
        var first = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

        entry.MarkFollowersNotified(first);
        entry.MarkFollowersNotified(first.AddHours(1));

        Assert.Equal(first, entry.FollowersNotifiedAt);
    }
}

public sealed class PriceChangeDigestTests
{
    private static PropertyPriceHistory Row(string field, decimal? oldValue, decimal? newValue, string currency = "USD", string? oldCurrency = null)
        => PropertyPriceHistory.Create(Guid.NewGuid(), field, oldValue, newValue, currency, Guid.NewGuid(), oldCurrency);

    [Fact]
    public void Nothing_pending_gives_nothing()
        => Assert.Null(HudhudNestApi.Application.Listings.PriceChangeDigest.Summarize([]));

    [Fact]
    public void Sale_price_is_announced_before_rent()
    {
        var sale = Row("PurchasePrice", 10m, 9m);
        var summary = HudhudNestApi.Application.Listings.PriceChangeDigest.Summarize([Row("ColdRent", 1m, 2m), sale]);

        Assert.NotNull(summary);
        Assert.Equal(sale.Id, summary!.AnnouncedRowId);
        Assert.Equal("detail.purchase_price", summary.Detail.Key);
    }

    [Fact]
    public void A_removed_price_is_not_announced()
        => Assert.Null(HudhudNestApi.Application.Listings.PriceChangeDigest.Summarize([Row("PurchasePrice", 10m, null)]));

    [Fact]
    public void A_currency_switch_with_the_same_number_is_announced()
        => Assert.NotNull(HudhudNestApi.Application.Listings.PriceChangeDigest.Summarize([Row("PurchasePrice", 10m, 10m, "EUR", "USD")]));
}
