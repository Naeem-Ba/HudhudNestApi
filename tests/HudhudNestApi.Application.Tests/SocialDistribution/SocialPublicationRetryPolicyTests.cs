using HudhudNestApi.Domain.SocialDistribution.Policies;

namespace HudhudNestApi.Application.Tests.SocialDistribution;

public sealed class SocialPublicationRetryPolicyTests
{
    [Fact]
    public void ComputeDelay_IsNeverNegative_AndGrowsWithRetryCount()
    {
        var first = SocialPublicationRetryPolicy.ComputeDelay(0);
        var second = SocialPublicationRetryPolicy.ComputeDelay(1);

        Assert.True(first > TimeSpan.Zero);
        // Compare the deterministic floor (jitter alone could not close a doubling gap).
        Assert.True(second.TotalMilliseconds > first.TotalMilliseconds * 1.5);
    }

    [Fact]
    public void ComputeDelay_NeverExceedsSixHoursByMuch_EvenForLargeRetryCounts()
    {
        var delay = SocialPublicationRetryPolicy.ComputeDelay(50);

        // Capped at 6h + at most 30s jitter.
        Assert.True(delay <= TimeSpan.FromHours(6) + TimeSpan.FromSeconds(31));
    }

    [Fact]
    public void ComputeDelay_NegativeInput_DoesNotThrow()
    {
        var delay = SocialPublicationRetryPolicy.ComputeDelay(-5);
        Assert.True(delay > TimeSpan.Zero);
    }
}
