using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Growth.Entities;
using Xunit;

namespace HudhudNestApi.Application.Tests.Growth;

public sealed class GrowthEventTests
{
    [Theory]
    [InlineData("Facebook", "facebook")]
    [InlineData("  google_ads ", "google_ads")]
    [InlineData("spring-2026.v2", "spring-2026.v2")]
    [InlineData("me@example.com", null)]
    [InlineData("https://evil.example", null)]
    [InlineData("two words", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Tags_keep_only_plain_values(string? input, string? expected) =>
        Assert.Equal(expected, GrowthEvent.Tag(input));

    [Fact]
    public void Long_tags_are_cut_to_64()
    {
        Assert.Equal(64, GrowthEvent.Tag(new string('a', 200))!.Length);
    }

    [Fact]
    public void Unknown_names_and_missing_keys_are_refused()
    {
        Assert.Throws<DomainException>(() => GrowthEvent.Create("made_up", "k", DateTime.UtcNow));
        Assert.Throws<DomainException>(() => GrowthEvent.Create(GrowthEventNames.UserRegistered, " ", DateTime.UtcNow));
    }

    [Fact]
    public void Every_name_is_short_snake_case()
    {
        Assert.All(GrowthEventNames.All, n =>
        {
            Assert.True(n.Length <= GrowthEvent.NameMaxLength);
            Assert.Matches("^[a-z]+(_[a-z]+)*$", n);
        });
    }
}

public sealed class GrowthOverviewWindowTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 15, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Default_is_the_last_30_days_up_to_the_end_of_today()
    {
        var (from, to) = HudhudNestApi.Application.Growth.GetGrowthOverviewQueryHandler.Window(null, null, Now);
        Assert.Equal(new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc), to);
        Assert.Equal(new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc), from);
    }

    [Fact]
    public void The_future_and_overlong_windows_are_cut()
    {
        var (from, to) = HudhudNestApi.Application.Growth.GetGrowthOverviewQueryHandler.Window(
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), Now);
        Assert.Equal(Now.Date.AddDays(1), to);
        Assert.Equal(186, (to - from).TotalDays);
    }
}
