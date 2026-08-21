using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings.Entities;

namespace PropertyApi.Application.Tests.Listings;

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
