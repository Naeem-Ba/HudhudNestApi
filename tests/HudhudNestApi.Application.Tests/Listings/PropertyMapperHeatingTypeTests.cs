using HudhudNestApi.Application.Listings.Mappers;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings.Entities;

namespace HudhudNestApi.Application.Tests.Listings;

/// <summary>
/// Core E2E audit F-2: PropertyDto did not expose HeatingType, so the edit form could not load it and
/// every save sent "Unknown" over the stored value.
/// </summary>
public sealed class PropertyMapperHeatingTypeTests
{
    [Theory(DisplayName = "PropertyDto exposes the stored heating type by enum name")]
    [InlineData(HeatingType.Unknown, "Unknown")]
    [InlineData(HeatingType.Gas, "Gas")]
    [InlineData(HeatingType.FloorHeating, "FloorHeating")]
    [InlineData(HeatingType.HeatPump, "HeatPump")]
    public void ToDto_ExposesHeatingType(HeatingType stored, string expected)
    {
        var property = Property.Create("Flat", "A flat to rent", Guid.NewGuid(), ListingType.ForRent);
        property.HeatingType = stored;

        var dto = PropertyMapper.ToDto(property);

        Assert.Equal(expected, dto.HeatingType);
    }
}
