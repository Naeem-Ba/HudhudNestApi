using FluentValidation.TestHelper;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.Properties.Validators;
using HudhudNestApi.Domain.Enums;

namespace HudhudNestApi.Application.Tests.Properties;

public sealed class PropertyFilterDtoValidatorTests
{
    private readonly PropertyFilterDtoValidator _validator = new();

    [Fact]
    public void DefaultFilter_ShouldPass()
    {
        var filter = new PropertyFilterDto();

        var result = _validator.TestValidate(filter);

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void SearchTermWithinLimit_ShouldPass()
    {
        var filter = new PropertyFilterDto { SearchTerm = "apartment damascus" };

        var result = _validator.TestValidate(filter);

        result.ShouldNotHaveValidationErrorFor(f => f.SearchTerm);
    }

    [Fact]
    public void SearchTermTooLong_ShouldFail()
    {
        var filter = new PropertyFilterDto { SearchTerm = new string('a', 201) };

        var result = _validator.TestValidate(filter);

        result.ShouldHaveValidationErrorFor(f => f.SearchTerm);
    }

    [Fact]
    public void EmptySearchTerm_ShouldPass()
    {
        // Empty/whitespace search must mean "no filter", not a validation error.
        var filter = new PropertyFilterDto { SearchTerm = "   " };

        var result = _validator.TestValidate(filter);

        result.ShouldNotHaveValidationErrorFor(f => f.SearchTerm);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("SYP")]
    public void ValidCurrencyCode_ShouldPass(string currencyCode)
    {
        var filter = new PropertyFilterDto { CurrencyCode = currencyCode };

        var result = _validator.TestValidate(filter);

        result.ShouldNotHaveValidationErrorFor(f => f.CurrencyCode);
    }

    [Fact]
    public void CurrencyCodeWrongLength_ShouldFail()
    {
        var filter = new PropertyFilterDto { CurrencyCode = "US" };

        var result = _validator.TestValidate(filter);

        result.ShouldHaveValidationErrorFor(f => f.CurrencyCode);
    }

    [Fact]
    public void MinPriceGreaterThanMaxPrice_ShouldFail()
    {
        var filter = new PropertyFilterDto { MinPrice = 500, MaxPrice = 100 };

        var result = _validator.TestValidate(filter);

        result.ShouldHaveValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void PageSizeOutOfRange_ShouldFail(int pageSize)
    {
        var filter = new PropertyFilterDto { PageSize = pageSize };

        var result = _validator.TestValidate(filter);

        result.ShouldHaveValidationErrorFor(f => f.PageSize);
    }

    // -- QA Phase 7 hardening ------------------------------------------------------------

    [Theory(DisplayName = "A NUL character in a free-text filter is refused instead of reaching PostgreSQL (P7-D03)")]
    [InlineData(nameof(PropertyFilterDto.SearchTerm))]
    [InlineData(nameof(PropertyFilterDto.City))]
    [InlineData(nameof(PropertyFilterDto.Region))]
    public void NulInText_ShouldFail(string property)
    {
        var filter = new PropertyFilterDto();
        typeof(PropertyFilterDto).GetProperty(property)!.SetValue(filter, "a\0b");

        var result = _validator.TestValidate(filter);

        result.ShouldHaveValidationErrorFor(property);
    }

    [Fact(DisplayName = "Tabs, newlines and Arabic text are still accepted in a search term")]
    public void OrdinaryText_ShouldPass()
    {
        var filter = new PropertyFilterDto { SearchTerm = "شقة\tمفروشة\nقرب الجامعة" };

        _validator.TestValidate(filter).ShouldNotHaveValidationErrorFor(f => f.SearchTerm);
    }

    [Theory(DisplayName = "A page beyond the result depth cap is refused, including int.MaxValue (P7-D03)")]
    [InlineData(2147483647, 20)]
    [InlineData(501, 20)]
    [InlineData(101, 100)]
    public void PageBeyondDepthCap_ShouldFail(int page, int pageSize)
    {
        var filter = new PropertyFilterDto { Page = page, PageSize = pageSize };

        var result = _validator.TestValidate(filter);

        result.ShouldHaveValidationErrorFor("Page");
    }

    [Theory]
    [InlineData(500, 20)]
    [InlineData(100, 100)]
    [InlineData(1, 100)]
    public void PageWithinDepthCap_ShouldPass(int page, int pageSize)
    {
        var filter = new PropertyFilterDto { Page = page, PageSize = pageSize };

        _validator.TestValidate(filter).ShouldNotHaveAnyValidationErrors();
    }

    [Fact(DisplayName = "MinRooms above MaxRooms is a validation error, not a silent empty list (P7-D07)")]
    public void ReversedRooms_ShouldFail()
    {
        var filter = new PropertyFilterDto { MinRooms = 5, MaxRooms = 2 };

        _validator.TestValidate(filter).ShouldHaveValidationErrorFor("MinRooms");
    }

    [Fact(DisplayName = "MinArea above MaxArea is a validation error, not a silent empty list (P7-D07)")]
    public void ReversedArea_ShouldFail()
    {
        var filter = new PropertyFilterDto { MinArea = 300, MaxArea = 100 };

        _validator.TestValidate(filter).ShouldHaveValidationErrorFor("MinArea");
    }

    [Theory(DisplayName = "Ordering by price needs a side of the market (P7-D02)")]
    [InlineData(null)]
    [InlineData(ListingType.ForRentAndSale)]
    public void PriceSortWithoutSaleOrRent_ShouldFail(ListingType? listingType)
    {
        var filter = new PropertyFilterDto { SortBy = "Price", ListingType = listingType };

        _validator.TestValidate(filter).ShouldHaveValidationErrorFor("SortBy");
    }

    [Theory]
    [InlineData(ListingType.ForSale)]
    [InlineData(ListingType.ForRent)]
    public void PriceSortWithSaleOrRent_ShouldPass(ListingType listingType)
    {
        var filter = new PropertyFilterDto { SortBy = "price", ListingType = listingType };

        _validator.TestValidate(filter).ShouldNotHaveAnyValidationErrors();
    }
}
