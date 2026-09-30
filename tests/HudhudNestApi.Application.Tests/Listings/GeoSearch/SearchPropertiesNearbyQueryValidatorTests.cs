using FluentValidation.TestHelper;
using HudhudNestApi.Application.Listings.DTOs;
using HudhudNestApi.Application.Listings.Queries.SearchPropertiesNearby;

namespace HudhudNestApi.Application.Tests.Listings.GeoSearch;

public sealed class SearchPropertiesNearbyQueryValidatorTests
{
    private readonly SearchPropertiesNearbyQueryValidator _validator = new();

    [Fact]
    public void ValidRequest_ShouldPass()
    {
        var query = new SearchPropertiesNearbyQuery(new GeoPropertySearchRequestDto
        {
            Latitude = 51.4566m,
            Longitude = 7.0123m,
            RadiusKm = 5m,
            Page = 1,
            PageSize = 20
        });

        var result = _validator.TestValidate(query);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(-91)]
    [InlineData(91)]
    public void InvalidLatitude_ShouldFail(decimal latitude)
    {
        var query = new SearchPropertiesNearbyQuery(new GeoPropertySearchRequestDto
        {
            Latitude = latitude,
            Longitude = 7.0123m,
            RadiusKm = 5m
        });

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.Filter.Latitude);
    }

    [Theory]
    [InlineData(-181)]
    [InlineData(181)]
    public void InvalidLongitude_ShouldFail(decimal longitude)
    {
        var query = new SearchPropertiesNearbyQuery(new GeoPropertySearchRequestDto
        {
            Latitude = 51.4566m,
            Longitude = longitude,
            RadiusKm = 5m
        });

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.Filter.Longitude);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50.1)]
    public void InvalidRadius_ShouldFail(decimal radiusKm)
    {
        var query = new SearchPropertiesNearbyQuery(new GeoPropertySearchRequestDto
        {
            Latitude = 51.4566m,
            Longitude = 7.0123m,
            RadiusKm = radiusKm
        });

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.Filter.RadiusKm);
    }

    [Fact]
    public void MinPriceGreaterThanMaxPrice_ShouldFail()
    {
        var query = new SearchPropertiesNearbyQuery(new GeoPropertySearchRequestDto
        {
            Latitude = 51.4566m,
            Longitude = 7.0123m,
            RadiusKm = 5m,
            MinPrice = 2000m,
            MaxPrice = 1000m
        });

        var result = _validator.TestValidate(query);

        result.ShouldHaveValidationErrorFor(x => x.Filter);
    }
}
