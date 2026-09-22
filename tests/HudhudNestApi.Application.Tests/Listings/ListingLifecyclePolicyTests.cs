using HudhudNestApi.Domain.Listings;
using Xunit;

namespace HudhudNestApi.Application.Tests.Listings;

/// <summary>
/// Regression guard for the business terms in ListingLifecyclePolicy — a customer-facing
/// promise (see the type's own doc comment), so an accidental edit here should fail loudly
/// rather than silently ship a different listing duration or warning window than the
/// pricing page/notification copy states.
///
/// Was: PublicationPeriod = 90 days (three months), ExpiryWarningLeadTime = 7 days.
/// Now: PublicationPeriod = 30 days (one month), ExpiryWarningLeadTime = 5 days — the two
/// values requested for the free listing duration and its expiry alert.
/// </summary>
public sealed class ListingLifecyclePolicyTests
{
    [Fact]
    public void PublicationPeriod_IsOneMonth()
    {
        Assert.Equal(TimeSpan.FromDays(30), ListingLifecyclePolicy.PublicationPeriod);
    }

    [Fact]
    public void ExpiryWarningLeadTime_IsFiveDays()
    {
        Assert.Equal(TimeSpan.FromDays(5), ListingLifecyclePolicy.ExpiryWarningLeadTime);
    }

    [Fact]
    public void ExpiryWarningLeadTime_IsShorterThanThePublicationPeriod()
    {
        // A warning that fires at or after the listing has already expired would never be
        // seen while the listing is still live — the whole point of warning ahead of time.
        Assert.True(
            ListingLifecyclePolicy.ExpiryWarningLeadTime < ListingLifecyclePolicy.PublicationPeriod);
    }

    [Fact]
    public void DeletionDueAt_IsExpiryPlusTheGracePeriod()
    {
        var expiresAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var deletionDueAt = ListingLifecyclePolicy.DeletionDueAt(expiresAt);

        Assert.Equal(
            expiresAt.Add(ListingLifecyclePolicy.GracePeriodBeforeDeletion),
            deletionDueAt);
    }
}
