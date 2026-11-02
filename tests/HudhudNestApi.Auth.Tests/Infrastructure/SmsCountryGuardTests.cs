using HudhudNestApi.Infrastructure.Auth.Services;

namespace HudhudNestApi.Auth.Tests.Infrastructure;

/// <summary>QA phase 17, P17-D01: an empty country list must not reach Production by accident.</summary>
[Trait("Category", "Security")]
[Trait("Feature", "SMS")]
public sealed class SmsCountryGuardTests
{
    private static SmsProviderOptions Settings(string[]? countries = null, bool anyCountry = false) => new()
    {
        Provider = "Http",
        ApiUrl = "https://sms-provider.example/api/send",
        ApiKey = "secret",
        FromNumber = "HudhudNest",
        AllowedCountryCodes = countries ?? [],
        AllowAnyCountry = anyCountry
    };

    [Fact]
    public void Production_RefusesToStart_WhenNoCountryIsListedAndAnyCountryIsNotChosen()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Settings().ValidateForEnvironment("Production"));
        Assert.Contains("AllowedCountryCodes", ex.Message);
    }

    [Fact]
    public void Production_RefusesToStart_WhenTheListHoldsOnlyBlanks()
    {
        Assert.Throws<InvalidOperationException>(() => Settings(["", "  "]).ValidateForEnvironment("Production"));
    }

    [Fact]
    public void Production_Starts_WithAListedCountry()
    {
        Assert.Null(Record.Exception(() => Settings(["+963"]).ValidateForEnvironment("Production")));
    }

    [Fact]
    public void Production_Starts_WhenAnyCountryIsAllowedOnPurpose()
    {
        Assert.Null(Record.Exception(() => Settings(null, anyCountry: true).ValidateForEnvironment("Production")));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    public void OtherEnvironments_AreNotAffected(string environment)
    {
        Assert.Null(Record.Exception(() => Settings().ValidateForEnvironment(environment)));
    }

    [Fact]
    public void TwilioInProduction_IsCoveredToo()
    {
        var twilio = new SmsProviderOptions { Provider = "Twilio" };
        Assert.Throws<InvalidOperationException>(() => twilio.ValidateForEnvironment("Production"));
    }

    [Fact]
    public void TheDailyCapDefaultsToOn()
    {
        Assert.Equal(300, new SmsProviderOptions().DailySendLimit);
    }
}
