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
