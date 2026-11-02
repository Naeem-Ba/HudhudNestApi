using System.Text.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Commands.CreateProperty;
using HudhudNestApi.Application.Listings.Commands.UpdateProperty;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Listings.Mappers;
using HudhudNestApi.Application.Listings.Queries.GetPropertiesList;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;
using Xunit;

namespace HudhudNestApi.Application.Tests.Listings;

/// <summary>
/// "Price is negotiable" (sale and rent): stored on the listing, editable, returned by the detail and
/// management DTOs, and absent from list/card payloads (the app shows it on the detail page only).
/// </summary>
public sealed class PriceNegotiableTests
{
    private static readonly JsonSerializerOptions BodyOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static Property Listing(ListingType type, bool negotiable)
    {
        var p = Property.Create("Flat", "A long enough description", Guid.NewGuid(), type, currencyCode: "USD");
        p.IsPriceNegotiable = negotiable;
        return p;
    }

    [Theory]
    [InlineData(ListingType.ForSale, true)]
    [InlineData(ListingType.ForRent, true)]
    [InlineData(ListingType.ForSale, false)]
    [InlineData(ListingType.ForRent, false)]
    public void Mapper_returns_the_flag(ListingType type, bool negotiable)
        => Assert.Equal(negotiable, PropertyMapper.ToDto(Listing(type, negotiable)).IsPriceNegotiable);

    [Fact]
    public void A_new_listing_is_not_negotiable_by_default()
        => Assert.False(Property.Create("Flat", "A long enough description", Guid.NewGuid(), ListingType.ForSale).IsPriceNegotiable);

    [Fact]
    public void Create_body_without_the_flag_means_not_negotiable()
    {
        var withFlag = JsonSerializer.Deserialize<CreatePropertyCommand>(
            """{"isPriceNegotiable":true}""", BodyOptions);
        var without = JsonSerializer.Deserialize<CreatePropertyCommand>("{}", BodyOptions);
        Assert.True(withFlag!.IsPriceNegotiable);
        Assert.False(without!.IsPriceNegotiable);
    }

    [Fact]
    public async Task Public_list_does_not_carry_the_flag()
    {
        var repo = new Mock<IPropertyRepository>();
        var page = new PagedResult<Property>
        {
            Items = new[] { Listing(ListingType.ForSale, true), Listing(ListingType.ForRent, true) },
            TotalCount = 2,
            Page = 1,
            PageSize = 20
        };
        repo.Setup(r => r.GetPagedWithViewCountsAsync(It.IsAny<PropertyFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((page, (IReadOnlyDictionary<Guid, long>)new Dictionary<Guid, long>()));

        var result = await new GetPropertiesListQueryHandler(repo.Object)
            .Handle(new GetPropertiesListQuery(new PropertyFilterDto()), CancellationToken.None);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.Null(i.IsPriceNegotiable));
    }

    private static (UpdatePropertyCommandHandler Sut, Property Property, Guid OwnerId) BuildUpdate(bool initial)
    {
        var ownerId = Guid.NewGuid();
        var property = Property.Create("شقة للبيع", "وصف الشقة", ownerId, ListingType.ForSale);
        property.IsPriceNegotiable = initial;
        var ownership = new Mock<IPropertyOwnershipService>();
        ownership
            .Setup(x => x.GetOwnedPropertyOrThrowAsync(property.Id, ownerId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);
        var sut = new UpdatePropertyCommandHandler(
            Mock.Of<IPropertyRepository>(), ownership.Object, Mock.Of<IPropertyPriceHistoryRepository>(),
            Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(), Mock.Of<ILocationSuggestionService>(),
            Mock.Of<ILocationHierarchyChecker>(), Mock.Of<ICommonLookupService>(), Mock.Of<IPublisher>(),
            NullLogger<UpdatePropertyCommandHandler>.Instance, Mock.Of<IListingQuotaGuard>(), Mock.Of<IAgencyRepository>());
        return (sut, property, ownerId);
    }

    private static UpdatePropertyCommand Body(string json, Guid id, Guid owner)
        => JsonSerializer.Deserialize<UpdatePropertyCommand>(json, BodyOptions)! with { PropertyId = id, RequestingUserId = owner };

    [Theory]
    [InlineData(false, "true", true)]
    [InlineData(true, "false", false)]
    public async Task Update_sets_the_flag(bool initial, string json, bool expected)
    {
        var (sut, property, owner) = BuildUpdate(initial);
        await sut.Handle(Body($$"""{ "isPriceNegotiable": {{json}} }""", property.Id, owner), CancellationToken.None);
        Assert.Equal(expected, property.IsPriceNegotiable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Update_without_the_flag_keeps_the_stored_value(bool initial)
    {
        var (sut, property, owner) = BuildUpdate(initial);
        await sut.Handle(Body("{}", property.Id, owner), CancellationToken.None);
        Assert.Equal(initial, property.IsPriceNegotiable);
    }
}
