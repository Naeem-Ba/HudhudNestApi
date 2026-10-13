using System.Text.Json;
using HudhudNestApi.Application.Common;
using Xunit;

namespace HudhudNestApi.Application.Tests.Common;

public sealed class UtcDateTimeExtensionsTests
{
    private static readonly DateTime Stored = new(2026, 11, 12, 10, 44, 54, DateTimeKind.Unspecified);

    [Fact]
    public void AsUtc_MarksAStoredValueUtc_WithoutShiftingTheClock()
    {
        var result = Stored.AsUtc();

        Assert.Equal(DateTimeKind.Utc, result.Kind);
        Assert.Equal(Stored.Ticks, result.Ticks);
    }

    [Fact]
    public void AsUtc_SerializesWithTheTrailingZ_WhichAnUnspecifiedValueLacks()
    {
        Assert.DoesNotContain("Z", JsonSerializer.Serialize(Stored), StringComparison.Ordinal);
        Assert.EndsWith("Z\"", JsonSerializer.Serialize(Stored.AsUtc()), StringComparison.Ordinal);
    }

    [Fact]
    public void AsUtc_LeavesAnAlreadyUtcValueAlone()
    {
        var utc = new DateTime(2026, 11, 12, 10, 44, 54, DateTimeKind.Utc);

        Assert.Equal(utc, utc.AsUtc());
        Assert.Equal(DateTimeKind.Utc, utc.AsUtc().Kind);
    }

    [Fact]
    public void AsUtc_KeepsNull()
    {
        DateTime? none = null;

        Assert.Null(none.AsUtc());
        Assert.Equal(DateTimeKind.Utc, ((DateTime?)Stored).AsUtc()!.Value.Kind);
    }
}
