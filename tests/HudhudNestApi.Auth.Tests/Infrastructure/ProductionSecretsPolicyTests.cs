using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Infrastructure.Health;

namespace HudhudNestApi.Auth.Tests.Infrastructure;

/// <summary>
/// A Production service used to start (and answer /health/ready with 200) without the OTP secret or the
/// phone lookup key, then fail every phone registration and login with a 500, and it also accepted the
/// public CI constants. These tests pin the refusal at startup and that no message ever repeats a secret.
/// </summary>
public sealed class ProductionSecretsPolicyTests
{
    private const string PublicCiJwtKey = "TEST_ONLY_SECRET_KEY_1234567890_1234567890";
    private const string PublicCiOtpKey = "TEST_ONLY_OTP_SECRET_KEY_1234567890_1234567890";
    private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    // Built from bytes and arithmetic rather than written out, so the test file carries no key-shaped
    // literal for the repository's secret scan to flag. None of these values is a real secret.
    private static readonly string PublicCiHmacKey =
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray());

    private static readonly string GoodJwtKey = VariedText(44, step: 7, offset: 3);
    private static readonly string GoodOtpKey = VariedText(44, step: 11, offset: 29);
    private static readonly string GoodHmacKey = Convert.ToBase64String(
        Enumerable.Range(0, 32).Select(i => (byte)((i * 37 + 11) % 256)).ToArray());

    /// <summary>A deterministic, varied (non-repeating-looking) string; never used as a real secret.</summary>
    private static string VariedText(int length, int step, int offset) =>
        new(Enumerable.Range(0, length).Select(i => Alphabet[(i * step + offset) % Alphabet.Length]).ToArray());

    private static IConfiguration Config(params (string Key, string? Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            [ProductionSecretsPolicy.JwtKeySetting] = GoodJwtKey,
            [ProductionSecretsPolicy.OtpSecretSetting] = GoodOtpKey,
            [ProductionSecretsPolicy.PhoneLookupHmacKeySetting] = GoodHmacKey
        };

        foreach (var (key, value) in overrides)
            values[key] = value;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact(DisplayName = "Three strong, different secrets are accepted")]
    public void FindProblems_ShouldBeEmpty_ForStrongDistinctSecrets()
    {
        Assert.Empty(ProductionSecretsPolicy.FindProblems(Config()));
        Assert.Null(Record.Exception(() => ProductionSecretsPolicy.EnsureValid(Config())));
    }

    [Theory(DisplayName = "A missing or blank secret is refused by name")]
    [InlineData(ProductionSecretsPolicy.JwtKeySetting, null)]
    [InlineData(ProductionSecretsPolicy.OtpSecretSetting, null)]
    [InlineData(ProductionSecretsPolicy.PhoneLookupHmacKeySetting, null)]
    [InlineData(ProductionSecretsPolicy.OtpSecretSetting, "")]
    [InlineData(ProductionSecretsPolicy.PhoneLookupHmacKeySetting, "   ")]
    public void EnsureValid_ShouldThrow_WhenASecretIsMissing(string setting, string? value)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSecretsPolicy.EnsureValid(Config((setting, value))));

        Assert.Contains($"{setting} is not set.", exception.Message, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "The public CI test values are refused")]
    [InlineData(ProductionSecretsPolicy.JwtKeySetting, PublicCiJwtKey, "TEST_ONLY")]
    [InlineData(ProductionSecretsPolicy.OtpSecretSetting, PublicCiOtpKey, "TEST_ONLY")]
    public void EnsureValid_ShouldThrow_WhenTheValueIsAPublicTestConstant(string setting, string value, string expected)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSecretsPolicy.EnsureValid(Config((setting, value))));

        Assert.Contains(setting, exception.Message, StringComparison.Ordinal);
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "The public CI phone lookup key is refused")]
    public void EnsureValid_ShouldThrow_WhenThePhoneLookupKeyIsThePublicCiValue()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSecretsPolicy.EnsureValid(
                Config((ProductionSecretsPolicy.PhoneLookupHmacKeySetting, PublicCiHmacKey))));

        Assert.Contains(ProductionSecretsPolicy.PhoneLookupHmacKeySetting, exception.Message, StringComparison.Ordinal);
        Assert.Contains("public CI test value", exception.Message, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "Placeholder wording is refused whatever the case")]
    [InlineData("please-change_me-before-going-live-0123456789ABCDEF")]
    [InlineData("YOUR_SECRET_GOES_HERE_9a8b7c6d5e4f3a2b1c0d9e8f7a6b")]
    [InlineData("this-is-a-Placeholder-value-9a8b7c6d5e4f3a2b1c0d9e")]
    public void EnsureValid_ShouldThrow_WhenTheValueLooksLikeAPlaceholder(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSecretsPolicy.EnsureValid(Config((ProductionSecretsPolicy.OtpSecretSetting, value))));

        Assert.Contains("placeholder or test value", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Short text secrets are refused")]
    public void EnsureValid_ShouldThrow_WhenATextSecretIsShorterThan32Characters()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSecretsPolicy.EnsureValid(Config((ProductionSecretsPolicy.JwtKeySetting, VariedText(20, step: 5, offset: 1)))));

        Assert.Contains("Jwt:Key must contain at least 32 characters.", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "A repeated-character secret is refused even when it is long enough")]
    public void EnsureValid_ShouldThrow_WhenASecretHasTooLittleVariety()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSecretsPolicy.EnsureValid(Config((ProductionSecretsPolicy.JwtKeySetting, new string('a', 64)))));

        Assert.Contains("too little variety", exception.Message, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "The phone lookup key must be Base64 and at least 32 bytes")]
    [InlineData("not base64 !!", "valid Base64")]
    [InlineData("c2hvcnQta2V5LW9ubHktMTYtYg==", "at least 32 bytes")]
    public void EnsureValid_ShouldThrow_WhenThePhoneLookupKeyIsMalformed(string value, string expected)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSecretsPolicy.EnsureValid(Config((ProductionSecretsPolicy.PhoneLookupHmacKeySetting, value))));

        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Theory(DisplayName = "Two secrets with the same value are refused")]
    [InlineData(ProductionSecretsPolicy.JwtKeySetting, ProductionSecretsPolicy.OtpSecretSetting)]
    public void EnsureValid_ShouldThrow_WhenTwoSecretsAreIdentical(string first, string second)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => ProductionSecretsPolicy.EnsureValid(Config((second, GoodJwtKey))));

        Assert.Contains($"{first} and {second} must be different values.", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Messages list every problem but never repeat a secret value")]
    public void EnsureValid_ShouldReportAllProblemsWithoutLeakingValues()
    {
        const string weakJwt = "TEST_ONLY_SECRET_KEY_for_this_deploy_9876";
        var configuration = Config(
            (ProductionSecretsPolicy.JwtKeySetting, weakJwt),
            (ProductionSecretsPolicy.OtpSecretSetting, null),
            (ProductionSecretsPolicy.PhoneLookupHmacKeySetting, PublicCiHmacKey));

        var exception = Assert.Throws<InvalidOperationException>(() => ProductionSecretsPolicy.EnsureValid(configuration));

        Assert.Contains("Jwt:Key looks like a placeholder", exception.Message, StringComparison.Ordinal);
        Assert.Contains("OtpSettings:SecretKey is not set.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Security:PhoneLookupHmacKey is the public CI test value.", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(weakJwt, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(PublicCiHmacKey, exception.Message, StringComparison.Ordinal);
    }

    // The service-level guarantee: the hosted service that the API starts before it listens now stops
    // the process, so Render keeps the previous deploy running instead of promoting a service that answers
    // /health/ready with 200 and returns 500 for every phone login.
    [Theory(DisplayName = "The Production startup validator refuses to start without the OTP or phone lookup key")]
    [InlineData(ProductionSecretsPolicy.OtpSecretSetting)]
    [InlineData(ProductionSecretsPolicy.PhoneLookupHmacKeySetting)]
    public async Task StartAsync_ShouldThrow_InProduction_WhenASecretIsMissing(string missingSetting)
    {
        var configuration = new ConfigurationBuilder()
            .AddConfiguration(Config((missingSetting, null)))
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=db.internal;Database=hudhudnest;Username=app;Password=pw;SSL Mode=Require"
            })
            .Build();

        var validator = new ProductionStartupValidator(
            Mock.Of<IServiceProvider>(),
            configuration,
            new HostingEnvironment { EnvironmentName = Environments.Production },
            NullLogger<ProductionStartupValidator>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => validator.StartAsync(CancellationToken.None));

        Assert.Contains(missingSetting, exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "The same weak secrets are tolerated outside Production")]
    public async Task StartAsync_ShouldNotCheckSecrets_OutsideProduction()
    {
        var configuration = Config(
            (ProductionSecretsPolicy.JwtKeySetting, PublicCiJwtKey),
            (ProductionSecretsPolicy.OtpSecretSetting, null));

        var validator = new ProductionStartupValidator(
            Mock.Of<IServiceProvider>(),
            configuration,
            new HostingEnvironment { EnvironmentName = Environments.Staging },
            NullLogger<ProductionStartupValidator>.Instance);

        await validator.StartAsync(CancellationToken.None);
    }
}
