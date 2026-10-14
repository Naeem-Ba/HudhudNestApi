using HudhudNestApi.Application.Search.Commands.CreateSavedSearch;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.Tests.Search;

/// <summary>QA phase 6 (P6-D04): an empty saved search matches every new listing, so it is refused.</summary>
public sealed class CreateSavedSearchCommandValidatorTests
{
    private static CreateSavedSearchCommand Command(
        string? name = null, string? city = null, int? governorateId = null, ListingType? listingType = null,
        decimal? maxPrice = null, string? currency = null) =>
        new(Guid.NewGuid(), name, null, city, null, governorateId, null, null, null, listingType,
            null, maxPrice, currency, null, null, null, null);

    private static readonly CreateSavedSearchCommandValidator Validator = new();

    [Fact]
    public void NoCriteria_IsRejected()
    {
        var result = Validator.Validate(Command());

        var error = Assert.Single(result.Errors);
        Assert.Equal("Criteria", error.PropertyName);
        Assert.Equal("SavedSearchCriteriaRequired", error.ErrorCode);
    }

    [Fact]
    public void OnlyANameOrCurrencyOrBlankText_IsRejected()
    {
        Assert.False(Validator.Validate(Command(name: "بحثي", currency: "USD", city: "   ")).IsValid);
    }

    [Fact]
    public void AnySingleCriterion_IsAccepted()
    {
        Assert.True(Validator.Validate(Command(city: "دمشق")).IsValid);
        Assert.True(Validator.Validate(Command(governorateId: 3)).IsValid);
        Assert.True(Validator.Validate(Command(listingType: ListingType.ForRent)).IsValid);
        Assert.True(Validator.Validate(Command(maxPrice: 500m)).IsValid);
    }
}
