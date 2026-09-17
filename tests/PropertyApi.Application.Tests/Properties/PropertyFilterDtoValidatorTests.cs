using FluentValidation.TestHelper;
using PropertyApi.Application.Properties.DTOs;
using PropertyApi.Application.Properties.Validators;

namespace PropertyApi.Application.Tests.Properties;

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

        result.ShouldHaveAnyValidationError();
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
}
