using FluentValidation.TestHelper;
using Moq;
using HudhudNestApi.Application.Common.DTOs;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Search.Commands.CreateSavedSearch;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.Tests.Search;

/// <summary>
/// QA Phase 12 (P12-D01, P12-D02): a saved search with an over-long text, a country code of the wrong length,
/// an unknown location or property type, or a range that cannot match anything was accepted and then failed at
/// the database as a 500, or sat in the background matcher forever.
/// </summary>
public sealed class SavedSearchPhase12ValidatorTests
{
    private static CreateSavedSearchCommandValidator CreateValidator(bool locationsConsistent = true, params int[] propertyTypeIds)
    {
        var locations = new Mock<ILocationHierarchyChecker>();
        locations
            .Setup(x => x.IsConsistentAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(locationsConsistent);

        var lookups = new Mock<ICommonLookupService>();
        lookups
            .Setup(x => x.GetPropertyTypeCatalogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(propertyTypeIds.Select(id => new PropertyTypeLookupDto(id, "t", "ن", "t", "Residential", null)).ToList());

        return new CreateSavedSearchCommandValidator(locations.Object, lookups.Object);
    }

    private static CreateSavedSearchCommand Command(
        string? name = null, string? city = "دمشق", string? region = null, int? governorateId = null,
        int? propertyTypeId = null, ListingType? listingType = null,
        decimal? minPrice = null, decimal? maxPrice = null,
        int? minRooms = null, int? maxRooms = null,
        decimal? minArea = null, decimal? maxArea = null) =>
        new(Guid.NewGuid(), name, null, city, region, governorateId, null, null, propertyTypeId, listingType,
            minPrice, maxPrice, null, minRooms, maxRooms, minArea, maxArea);

    [Fact]
    public async Task ValidSearch_Passes()
        => (await CreateValidator(true, 4).TestValidateAsync(
            Command(governorateId: 1, propertyTypeId: 4, minPrice: 100, maxPrice: 900, minRooms: 2, maxRooms: 4, minArea: 50, maxArea: 120)))
            .ShouldNotHaveAnyValidationErrors();

    [Fact]
    public async Task OverLongTexts_AreRejected()
    {
        var result = await CreateValidator().TestValidateAsync(
            Command(name: new string('ا', 151), city: new string('x', 151), region: new string('y', 151)));

        result.ShouldHaveValidationErrorFor(x => x.Name);
        result.ShouldHaveValidationErrorFor(x => x.City);
        result.ShouldHaveValidationErrorFor(x => x.Region);
    }

    [Fact]
    public async Task NulCharacter_IsRejected()
        => (await CreateValidator().TestValidateAsync(Command(city: "a\0b"))).ShouldHaveValidationErrorFor(x => x.City);

    [Fact]
    public async Task UnknownOrMismatchedLocation_IsRejected()
        => (await CreateValidator(locationsConsistent: false).TestValidateAsync(Command(governorateId: 999)))
            .ShouldHaveValidationErrorFor("Location");

    [Fact]
    public async Task UnknownPropertyType_IsRejected()
        => (await CreateValidator(true, 4).TestValidateAsync(Command(propertyTypeId: -5)))
            .ShouldHaveValidationErrorFor(x => x.PropertyTypeId);

    [Fact]
    public async Task UnknownListingType_IsRejected()
        => (await CreateValidator().TestValidateAsync(Command(listingType: (ListingType)99)))
            .ShouldHaveValidationErrorFor(x => x.ListingType);

    [Theory]
    [InlineData(-1d, null)]
    [InlineData(null, -1d)]
    [InlineData(900d, 100d)]
    [InlineData(null, 1e12)]
    public async Task ImpossiblePriceRange_IsRejected(double? min, double? max)
    {
        var result = await CreateValidator().TestValidateAsync(
            Command(minPrice: min is null ? null : (decimal)min, maxPrice: max is null ? null : (decimal)max));
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(5, 2)]
    [InlineData(-1, null)]
    [InlineData(null, 51)]
    public async Task ImpossibleRoomRange_IsRejected(int? min, int? max)
        => Assert.False((await CreateValidator().TestValidateAsync(Command(minRooms: min, maxRooms: max))).IsValid);

    [Theory]
    [InlineData(120d, 50d)]
    [InlineData(-1d, null)]
    [InlineData(null, 100_000_000d)]
    public async Task ImpossibleAreaRange_IsRejected(double? min, double? max)
    {
        var result = await CreateValidator().TestValidateAsync(
            Command(minArea: min is null ? null : (decimal)min, maxArea: max is null ? null : (decimal)max));
        Assert.False(result.IsValid);
    }
}
