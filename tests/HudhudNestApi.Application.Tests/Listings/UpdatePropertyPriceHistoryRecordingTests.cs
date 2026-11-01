using System.Text.Json;
using System.Text.Json.Serialization;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Commands.UpdateProperty;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HudhudNestApi.Application.Tests.Listings;

/// <summary>
/// Growth engine P1 (DECISIONS.md D-02): the price timeline only holds changes the public could see,
/// and a currency switch keeps the old currency instead of relabelling the old number.
/// </summary>
public sealed class UpdatePropertyPriceHistoryRecordingTests
{
    private static readonly JsonSerializerOptions BodyOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static (UpdatePropertyCommandHandler Sut, Property Property, Guid OwnerId, List<PropertyPriceHistory> Rows) Build(
        bool published, ListingType listingType = ListingType.ForSale, string currency = "USD")
    {
        var ownerId = Guid.NewGuid();
        var property = Property.Create("شقة", "وصف", ownerId, listingType, currencyCode: currency, isPublished: published);
        property.PurchasePrice = 100_000m;
        property.ColdRent = null;

        var ownership = new Mock<IPropertyOwnershipService>();
        ownership
            .Setup(x => x.GetOwnedPropertyOrThrowAsync(property.Id, ownerId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);

        var rows = new List<PropertyPriceHistory>();
        var history = new Mock<IPropertyPriceHistoryRepository>();
        history
            .Setup(h => h.AddAsync(It.IsAny<PropertyPriceHistory>(), It.IsAny<CancellationToken>()))
            .Callback<PropertyPriceHistory, CancellationToken>((row, _) => rows.Add(row))
            .Returns(Task.CompletedTask);

        var sut = new UpdatePropertyCommandHandler(
            Mock.Of<IPropertyRepository>(),
            ownership.Object,
            history.Object,
            Mock.Of<IUnitOfWork>(),
            Mock.Of<INotificationService>(),
            Mock.Of<ILocationSuggestionService>(),
            Mock.Of<ILocationHierarchyChecker>(),
            Mock.Of<ICommonLookupService>(),
            Mock.Of<IPublisher>(),
            NullLogger<UpdatePropertyCommandHandler>.Instance,
            Mock.Of<IListingQuotaGuard>(),
            Mock.Of<IAgencyRepository>());

        return (sut, property, ownerId, rows);
    }

    private static UpdatePropertyCommand Body(string json, Guid propertyId, Guid ownerId)
        => JsonSerializer.Deserialize<UpdatePropertyCommand>(json, BodyOptions)! with { PropertyId = propertyId, RequestingUserId = ownerId };

    [Fact]
    public async Task A_price_change_on_a_published_listing_writes_one_row()
    {
        var (sut, property, ownerId, rows) = Build(published: true);

        await sut.Handle(Body("""{ "purchasePrice": 90000 }""", property.Id, ownerId), CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("PurchasePrice", row.PriceField);
        Assert.Equal(100_000m, row.OldValue);
        Assert.Equal(90_000m, row.NewValue);
        Assert.Equal("USD", row.CurrencyCode);
        Assert.Null(row.OldCurrencyCode);
        Assert.True(row.IsComparable);
    }

    [Fact]
    public async Task A_draft_edit_is_not_a_price_change()
    {
        var (sut, property, ownerId, rows) = Build(published: false);

        await sut.Handle(Body("""{ "purchasePrice": 90000 }""", property.Id, ownerId), CancellationToken.None);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task Unpublishing_in_the_same_edit_records_nothing()
    {
        var (sut, property, ownerId, rows) = Build(published: true);

        await sut.Handle(Body("""{ "purchasePrice": 90000, "isPublished": false }""", property.Id, ownerId), CancellationToken.None);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task Unchanged_price_writes_nothing()
    {
        var (sut, property, ownerId, rows) = Build(published: true);

        await sut.Handle(Body("""{ "purchasePrice": 100000, "title": "عنوان" }""", property.Id, ownerId), CancellationToken.None);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task A_currency_switch_keeps_the_old_currency_even_when_the_number_stays()
    {
        var (sut, property, ownerId, rows) = Build(published: true);

        await sut.Handle(Body("""{ "currencyCode": "eur" }""", property.Id, ownerId), CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(100_000m, row.OldValue);
        Assert.Equal(100_000m, row.NewValue);
        Assert.Equal("EUR", row.CurrencyCode);
        Assert.Equal("USD", row.OldCurrencyCode);
        Assert.False(row.IsComparable);
    }
}
