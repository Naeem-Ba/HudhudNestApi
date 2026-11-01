using HudhudNestApi.Application.Listings.Map;
using HudhudNestApi.Application.Listings.Mappers;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings;
using HudhudNestApi.Domain.Listings.Entities;
using Xunit;

namespace HudhudNestApi.Application.Tests.Listings;

public sealed class LocationPrivacyTests
{
    [Theory]
    [InlineData(33.51384, 33.5125)]
    [InlineData(33.51001, 33.5125)]
    [InlineData(33.50999, 33.5075)]
    [InlineData(-0.0001, -0.0025)]
    public void Approximate_snaps_to_the_cell_centre(double input, double expected) =>
        Assert.Equal((decimal)expected, LocationPrivacy.Approximate((decimal)input));

    [Fact]
    public void Two_points_in_one_cell_look_the_same() =>
        Assert.Equal(LocationPrivacy.Approximate(36.2011m), LocationPrivacy.Approximate(36.2049m));

    private static Property Listing(bool hide)
    {
        var p = Property.Create("Flat", "A long enough description", Guid.NewGuid(), ListingType.ForRent, currencyCode: "USD");
        p.Street = "Baghdad Street 12";
        p.Latitude = 33.51384m;
        p.Longitude = 36.29128m;
        p.HideExactLocation = hide;
        return p;
    }

    [Fact]
    public void Public_view_of_a_hidden_spot_is_approximate_and_has_no_street()
    {
        var dto = PropertyMapper.ToDto(Listing(hide: true));
        Assert.True(dto.IsLocationApproximate);
        Assert.Equal(33.5125m, dto.Latitude);
        Assert.Equal(36.2925m, dto.Longitude);
        Assert.Equal(string.Empty, dto.Street);
        Assert.Null(dto.HideExactLocation);
    }

    [Fact]
    public void Owner_view_keeps_the_exact_pin_and_shows_the_choice()
    {
        var dto = PropertyMapper.ToDto(Listing(hide: true), revealExactLocation: true);
        Assert.False(dto.IsLocationApproximate);
        Assert.Equal(33.51384m, dto.Latitude);
        Assert.Equal("Baghdad Street 12", dto.Street);
        Assert.True(dto.HideExactLocation);
    }

    [Fact]
    public void Listings_that_do_not_hide_are_unchanged()
    {
        var dto = PropertyMapper.ToDto(Listing(hide: false));
        Assert.False(dto.IsLocationApproximate);
        Assert.Equal(33.51384m, dto.Latitude);
        Assert.Equal("Baghdad Street 12", dto.Street);
    }

    [Theory]
    [InlineData(1, 45)]
    [InlineData(10, 0.087890625)]
    public void Cluster_cell_shrinks_with_zoom(int zoom, double degrees) =>
        Assert.Equal((decimal)degrees, GetMapListingsQuery.CellDegrees(zoom));

    [Fact]
    public void Map_box_must_be_valid()
    {
        var v = new GetMapListingsQueryValidator();
        Assert.True(v.Validate(new GetMapListingsQuery(33, 36, 34, 37, 10, null, null, null)).IsValid);
        Assert.False(v.Validate(new GetMapListingsQuery(34, 36, 33, 37, 10, null, null, null)).IsValid);
        Assert.False(v.Validate(new GetMapListingsQuery(33, 36, 34, 37, 0, null, null, null)).IsValid);
        Assert.False(v.Validate(new GetMapListingsQuery(-95, 36, 34, 37, 5, null, null, null)).IsValid);
    }
}
