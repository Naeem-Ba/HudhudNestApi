using HudhudNestApi.Infrastructure.Audit;

namespace HudhudNestApi.Infrastructure.Tests.Audit;

public sealed class AuditIpMaskTests
{
    [Theory(DisplayName = "D07: IPv4 loses its last octet, IPv6 keeps its first 48 bits")]
    [InlineData("203.0.113.57", "203.0.113.0")]
    [InlineData(" 10.1.2.3 ", "10.1.2.0")]
    [InlineData("::ffff:198.51.100.9", "198.51.100.0")]
    [InlineData("2001:db8:abcd:1234:5678:9abc:def0:1234", "2001:db8:abcd::")]
    public void Masks(string input, string expected) => Assert.Equal(expected, AuditIpMask.Mask(input));

    [Theory(DisplayName = "D07: anything that is not an address is dropped, never stored raw")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-ip")]
    [InlineData("1.2.3")]
    public void DropsGarbage(string? input) => Assert.Null(AuditIpMask.Mask(input));
}
