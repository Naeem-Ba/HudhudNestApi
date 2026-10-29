using Microsoft.Extensions.Configuration;
using HudhudNestApi.Infrastructure.Listings;
using Xunit;

namespace HudhudNestApi.Infrastructure.Tests;

public sealed class ListingViewPolicyTests
{
    private const string Key = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    private static ListingViewPolicy Create(params (string, string?)[] settings)
        => new(new ConfigurationBuilder()
            .AddInMemoryCollection(new[] { ("Security:PhoneLookupHmacKey", (string?)Key) }.Concat(settings)
                .Select(s => new KeyValuePair<string, string?>(s.Item1, s.Item2)))
            .Build());

    [Fact]
    public void Defaults_AreADayOfCooldownAndNinetyDaysOfHistory()
    {
        var policy = Create();
        Assert.Equal(TimeSpan.FromHours(24), policy.Cooldown);
        Assert.Equal(90, policy.RetentionDays);
    }

    [Fact]
    public void Settings_AreClamped()
    {
        var policy = Create(("ListingViews:CooldownHours", "0"), ("ListingViews:RetentionDays", "5"));
        Assert.Equal(TimeSpan.FromHours(1), policy.Cooldown);
        Assert.Equal(30, policy.RetentionDays);

        var big = Create(("ListingViews:CooldownHours", "100000"), ("ListingViews:RetentionDays", "100000"));
        Assert.Equal(TimeSpan.FromHours(168), big.Cooldown);
        Assert.Equal(365, big.RetentionDays);
    }

    [Fact]
    public void Bucket_ChangesOnlyAtWindowBoundaries()
    {
        var policy = Create(("ListingViews:CooldownHours", "6"));
        var start = new DateTime(2026, 10, 29, 6, 0, 0, DateTimeKind.Utc);
        var bucket = policy.BucketFor(start);

        Assert.Equal(bucket, policy.BucketFor(start.AddHours(5).AddMinutes(59)));
        Assert.Equal(bucket + 1, policy.BucketFor(start.AddHours(6)));
        Assert.Equal(bucket - 1, policy.BucketFor(start.AddSeconds(-1)));
    }

    [Fact]
    public void Hash_IsStable_KeyedAndNeverContainsTheIdentity()
    {
        var policy = Create();
        var a = policy.HashViewer("v:abcdefghijklmnop");

        Assert.Equal(a, policy.HashViewer("v:abcdefghijklmnop"));
        Assert.NotEqual(a, policy.HashViewer("v:abcdefghijklmnoq"));
        Assert.Equal(64, a.Length);
        Assert.DoesNotContain("abcdefghijklmnop", a);

        var otherKey = new ListingViewPolicy(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Security:PhoneLookupHmacKey"] = Convert.ToBase64String(new byte[32]) }).Build());
        Assert.NotEqual(a, otherKey.HashViewer("v:abcdefghijklmnop"));
    }

    [Fact]
    public void WithoutAnySecret_TheHashFailsLoudly()
    {
        var policy = new ListingViewPolicy(new ConfigurationBuilder().Build());
        Assert.Throws<InvalidOperationException>(() => policy.HashViewer("v:x"));
    }
}
