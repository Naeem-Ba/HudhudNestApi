using System.Text.Json;
using System.Text.Json.Serialization;
using HudhudNestApi.Application.Listings.Commands.UpdateProperty;
using HudhudNestApi.Application.Listings.Validators;

namespace HudhudNestApi.Application.Tests.Listings;

/// <summary>QA Phase 8, P8-D01: an update must not reach the database with a value it cannot store.</summary>
public sealed class UpdatePropertyInputLimitsTests
{
    private static readonly UpdatePropertyCommandValidator Validator = new();

    private static readonly JsonSerializerOptions BodyOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static UpdatePropertyCommand Body(string json)
        => JsonSerializer.Deserialize<UpdatePropertyCommand>(json, BodyOptions)!;

    [Theory]
    [InlineData("coldRent")]
    [InlineData("warmRent")]
    [InlineData("purchasePrice")]
    [InlineData("deposit")]
    [InlineData("additionalCosts")]
    public void MoneyAtTheCap_IsRefused(string field)
        => Assert.Contains(
            Validator.Validate(Body($$"""{"{{field}}": 1000000000000}""")).Errors,
            e => string.Equals(e.PropertyName, field, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void AreaAtTheColumnLimit_IsRefused()
        => Assert.Contains(Validator.Validate(Body("""{"area": 100000000}""")).Errors, e => e.PropertyName == "Area");

    [Theory]
    [InlineData("title")]
    [InlineData("description")]
    [InlineData("street")]
    [InlineData("city")]
    [InlineData("region")]
    [InlineData("postalCode")]
    [InlineData("districtText")]
    [InlineData("neighborhoodText")]
    public void NulCharacter_IsRefused(string field)
        => Assert.Contains(
            Validator.Validate(Body($$"""{"{{field}}": "ab\u0000cd"}""")).Errors,
            e => string.Equals(e.PropertyName, field, StringComparison.OrdinalIgnoreCase) && e.ErrorCode == "INVALID_CHARACTER");

    [Fact]
    public void OrdinaryValues_StillPass()
        => Assert.Empty(Validator.Validate(
            Body("""{"title": "شقة", "purchasePrice": 250000, "area": 120}""") with
            {
                PropertyId = Guid.NewGuid(),
                RequestingUserId = Guid.NewGuid()
            }).Errors);
}
