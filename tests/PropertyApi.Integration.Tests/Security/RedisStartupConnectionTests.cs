using Microsoft.Extensions.Configuration;
using PropertyApi.Security.RateLimiting;

namespace PropertyApi.Integration.Tests.Security;

/// <summary>
/// The Redis connection is established eagerly at startup with a bounded wait so that the first
/// rate-limited requests on a fresh instance are not rejected by the limiter's fail-closed 503.
/// </summary>
public sealed class RedisStartupConnectionTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(5);

    [Fact(DisplayName = "An already connected multiplexer returns immediately")]
    public async Task WaitUntilConnectedAsync_AlreadyConnected_ReturnsTrue()
    {
        var connected = await RedisStartupConnection.WaitUntilConnectedAsync(
            () => true, TimeSpan.Zero, Poll);

        Assert.True(connected);
    }

    [Fact(DisplayName = "A connection that comes up within the budget is awaited")]
    public async Task WaitUntilConnectedAsync_ConnectsAfterSeveralPolls_ReturnsTrue()
    {
        var polls = 0;

        var connected = await RedisStartupConnection.WaitUntilConnectedAsync(
            () => ++polls > 3, TimeSpan.FromSeconds(10), Poll);

        Assert.True(connected);
        Assert.Equal(4, polls);
    }

    [Fact(DisplayName = "A connection that never comes up gives up after the budget instead of hanging")]
    public async Task WaitUntilConnectedAsync_NeverConnects_ReturnsFalseAfterBudget()
    {
        var connected = await RedisStartupConnection.WaitUntilConnectedAsync(
            () => false, TimeSpan.FromMilliseconds(50), Poll);

        Assert.False(connected);
    }

    [Theory(DisplayName = "The startup timeout is configurable and bounded")]
    [InlineData(null, 15)]
    [InlineData("0", 0)]
    [InlineData("30", 30)]
    [InlineData("-5", 0)]
    [InlineData("600", 60)]
    public void ResolveTimeoutSeconds_UsesDefaultAndClamps(string? configured, int expected)
    {
        var values = new Dictionary<string, string?>();
        if (configured is not null)
        {
            values[RedisStartupConnection.TimeoutConfigurationKey] = configured;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        Assert.Equal(expected, RedisStartupConnection.ResolveTimeoutSeconds(configuration));
    }
}
