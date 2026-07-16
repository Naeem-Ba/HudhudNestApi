using PropertyApi.Infrastructure.Auth.Security;

namespace PropertyApi.Auth.Tests.Infrastructure;

public sealed class E164PhoneNumberNormalizerTests
{
    private readonly E164PhoneNumberNormalizer _sut = new();

    [Theory]
    [InlineData("+4915112345678")]
    [InlineData(" +963911234567 ")]
    public void Normalize_ValidE164_ReturnsCanonicalValue(string input)
    {
        var result = _sut.Normalize(input);
        Assert.True(result.Succeeded);
        Assert.Equal(input.Trim(), result.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("004915112345678")]
    [InlineData("+012345678")]
    [InlineData("+123")]
    public void Normalize_InvalidInput_ReturnsStableError(string? input)
    {
        var result = _sut.Normalize(input);
        Assert.False(result.Succeeded);
        Assert.Equal("PHONE_NUMBER_INVALID", result.ErrorCode);
    }
}
