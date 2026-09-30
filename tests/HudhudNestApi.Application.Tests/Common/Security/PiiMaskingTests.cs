using HudhudNestApi.Application.Common.Security;
using Xunit;

namespace HudhudNestApi.Application.Tests.Common.Security;

public sealed class PiiMaskingTests
{
    [Theory]
    [InlineData("john.doe@example.com", "j******@example.com")]
    [InlineData("a@example.com", "*@example.com")]
    public void MaskEmail_HidesLocalPart_ButKeepsDomain(
        string email,
        string expected)
    {
        Assert.Equal(
            expected,
            PiiMasking.MaskEmail(email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MaskEmail_ReturnsPlaceholder_ForNullOrBlank(
        string? email)
    {
        Assert.Equal(
            "(none)",
            PiiMasking.MaskEmail(email));
    }

    [Fact]
    public void MaskEmail_NeverContainsTheOriginalLocalPart()
    {
        var masked =
            PiiMasking.MaskEmail("verysecretname@example.com");

        Assert.DoesNotContain(
            "verysecretname",
            masked,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("+491701234567", "***67")]
    [InlineData("12", "***")]
    public void MaskPhone_KeepsOnlyLastTwoDigits(
        string phone,
        string expected)
    {
        Assert.Equal(
            expected,
            PiiMasking.MaskPhone(phone));
    }

    [Fact]
    public void MaskPhone_NeverContainsTheFullOriginalNumber()
    {
        var masked =
            PiiMasking.MaskPhone("+963991112233");

        Assert.DoesNotContain(
            "+963991112233",
            masked,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("203.0.113.42", "203.0.113.0")]
    [InlineData("10.0.0.1", "10.0.0.0")]
    public void MaskIp_ZeroesLastIPv4Octet(
        string ip,
        string expected)
    {
        Assert.Equal(
            expected,
            PiiMasking.MaskIp(ip));
    }

    [Fact]
    public void MaskIp_ReturnsPlaceholder_ForNullOrBlank()
    {
        Assert.Equal(
            "(none)",
            PiiMasking.MaskIp(null));
    }

    [Fact]
    public void MaskIpsInText_MasksEmbeddedIpv4_InsideACompositeRateLimitPartitionKey()
    {
        var masked =
            PiiMasking.MaskIpsInText(
                "user:3fae9c21-1111-2222-3333-abcdefabcdef:ip:203.0.113.42");

        Assert.DoesNotContain(
            "203.0.113.42",
            masked,
            StringComparison.Ordinal);

        Assert.Contains(
            "203.0.113.0",
            masked,
            StringComparison.Ordinal);

        // The non-IP structure of the key must survive untouched, otherwise an
        // operator can no longer tell which user/policy bucket was throttled.
        Assert.StartsWith(
            "user:3fae9c21-1111-2222-3333-abcdefabcdef:ip:",
            masked,
            StringComparison.Ordinal);
    }
}
