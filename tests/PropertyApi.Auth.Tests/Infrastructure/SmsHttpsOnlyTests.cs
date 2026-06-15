namespace PropertyApi.Auth.Tests.Infrastructure;

[Trait("Category", "Security")]
[Trait("Feature", "SMS")]
public sealed class SmsHttpsOnlyTests
{
    [Fact(DisplayName = "Production accepts HTTPS SMS endpoint")]
    public void SmsEndpoint_ShouldAccept_HttpsInProduction()
    {
        var settings = CreateValidSettings("https://sms-provider.example/api/send");

        var exception = Record.Exception(() => settings.ValidateForEnvironment("Production"));

        Assert.Null(exception);
    }

    [Fact(DisplayName = "Production rejects plain HTTP SMS endpoint")]
    public void SmsEndpoint_ShouldThrow_IfHttpInProduction()
    {
        var settings = CreateValidSettings("http://sms-provider.example/api/send");

        var exception = Assert.Throws<InvalidOperationException>(
            () => settings.ValidateForEnvironment("Production"));

        Assert.True(
            exception.Message.Contains("HTTPS", StringComparison.OrdinalIgnoreCase),
            $"Expected the exception message to mention HTTPS. Message: {exception.Message}");
    }

    [Fact(DisplayName = "Development allows HTTP SMS endpoint")]
    public void SmsEndpoint_ShouldNotThrow_IfHttpInDevelopment()
    {
        var settings = CreateValidSettings("http://localhost:8080/api/send");

        var exception = Record.Exception(() => settings.ValidateForEnvironment("Development"));

        Assert.Null(exception);
    }

    [Theory(DisplayName = "Production requires all required SMS provider fields")]
    [InlineData("", "https://sms-provider.example/api/send", "secret", "+491234567890")]
    [InlineData("Http", "", "secret", "+491234567890")]
    [InlineData("Http", "https://sms-provider.example/api/send", "", "+491234567890")]
    [InlineData("Http", "https://sms-provider.example/api/send", "secret", "")]
    public void SmsEndpoint_ShouldThrow_IfRequiredProductionSettingIsMissing(
        string provider,
        string apiUrl,
        string apiKey,
        string fromNumber)
    {
        var settings = new SmsProviderOptions
        {
            Provider = provider,
            ApiUrl = apiUrl,
            ApiKey = apiKey,
            FromNumber = fromNumber
        };

        Assert.Throws<InvalidOperationException>(
            () => settings.ValidateForEnvironment("Production"));
    }

    private static SmsProviderOptions CreateValidSettings(string apiUrl)
    {
        return new SmsProviderOptions
        {
            Provider = "Http",
            ApiUrl = apiUrl,
            ApiKey = "test-api-key",
            FromNumber = "+491234567890"
        };
    }
}
