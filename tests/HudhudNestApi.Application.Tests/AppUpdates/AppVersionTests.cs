using HudhudNestApi.Domain.AppUpdates.ValueObjects;

namespace HudhudNestApi.Application.Tests.AppUpdates;

/// <summary>
/// The correctness of the whole App Update Management feature hinges on this type comparing
/// numerically, never as a string — "1.10.0" must sort after "1.9.9" even though '1' &lt; '9'
/// lexicographically.
/// </summary>
public sealed class AppVersionTests
{
    [Theory]
    [InlineData("1.0.0", 1, 0, 0)]
    [InlineData("0.0.1", 0, 0, 1)]
    [InlineData("10.20.30", 10, 20, 30)]
    public void TryParse_ValidVersions_Succeeds(string input, int major, int minor, int patch)
    {
        Assert.True(AppVersion.TryParse(input, out var version));
        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(patch, version.Patch);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3-beta")]
    [InlineData("01.2.3")]
    [InlineData("-1.2.3")]
    [InlineData("a.b.c")]
    [InlineData(" 1.2.3 ")]
    public void TryParse_InvalidVersions_Fails(string? input)
    {
        Assert.False(AppVersion.TryParse(input, out _));
    }

    [Fact]
    public void Comparison_1_0_0_LessThan_1_1_0()
    {
        Assert.True(AppVersion.Parse("1.0.0") < AppVersion.Parse("1.1.0"));
    }

    [Fact]
    public void Comparison_1_1_0_LessThan_1_10_0()
    {
        Assert.True(AppVersion.Parse("1.1.0") < AppVersion.Parse("1.10.0"));
    }

    [Fact]
    public void Comparison_1_9_9_LessThan_1_10_0()
    {
        Assert.True(AppVersion.Parse("1.9.9") < AppVersion.Parse("1.10.0"));
    }

    [Fact]
    public void Comparison_1_10_0_EqualTo_1_10_0()
    {
        Assert.Equal(AppVersion.Parse("1.10.0"), AppVersion.Parse("1.10.0"));
        Assert.True(AppVersion.Parse("1.10.0") == AppVersion.Parse("1.10.0"));
    }

    [Fact]
    public void Comparison_2_0_0_GreaterThan_1_99_99()
    {
        Assert.True(AppVersion.Parse("2.0.0") > AppVersion.Parse("1.99.99"));
    }

    [Fact]
    public void ToString_RoundTrips_CanonicalForm()
    {
        Assert.Equal("1.2.3", AppVersion.Parse("1.2.3").ToString());
    }

    [Fact]
    public void Parse_InvalidVersion_Throws()
    {
        Assert.Throws<FormatException>(() => AppVersion.Parse("not-a-version"));
    }
}
