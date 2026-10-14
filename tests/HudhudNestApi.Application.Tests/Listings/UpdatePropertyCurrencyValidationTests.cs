using System.Text.Json;
using System.Text.Json.Serialization;
using HudhudNestApi.Application.Listings.Commands.UpdateProperty;
using HudhudNestApi.Application.Listings.Validators;

namespace HudhudNestApi.Application.Tests.Listings;

/// <summary>Listings are priced in the dollar, the euro or the Syrian pound only.</summary>
public sealed class UpdatePropertyCurrencyValidationTests
{
    private static readonly UpdatePropertyCommandValidator Validator = new();

    private static readonly JsonSerializerOptions BodyOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static UpdatePropertyCommand Body(string json)
        => JsonSerializer.Deserialize<UpdatePropertyCommand>(json, BodyOptions)!;

    [Theory]
    [InlineData("USD")]
    [InlineData("eur")]
    [InlineData("SYP")]
    public void MarketCurrency_IsAccepted(string currency)
        => Assert.DoesNotContain(Validator.Validate(Body($$"""{"currencyCode":"{{currency}}"}""")).Errors, e => e.PropertyName == "CurrencyCode");

    [Theory]
    [InlineData("TRY")]
    [InlineData("AED")]
    [InlineData("SAR")]
    [InlineData("GBP")]
    public void OtherCurrency_IsRefused(string currency)
        => Assert.Contains(Validator.Validate(Body($$"""{"currencyCode":"{{currency}}"}""")).Errors, e => e.PropertyName == "CurrencyCode");

    [Fact(DisplayName = "A status-only update (no currency in the body) is not blocked, so listings priced in an old currency can still be reserved or sold")]
    public void StatusOnlyUpdate_DoesNotTouchCurrency()
        => Assert.DoesNotContain(Validator.Validate(Body("""{"status":"Reserved"}""")).Errors, e => e.PropertyName == "CurrencyCode");
}
