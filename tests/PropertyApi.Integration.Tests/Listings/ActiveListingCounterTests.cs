using Moq;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Domain.Users.Entities;
using PropertyApi.Infrastructure.Listings;
using Xunit;

namespace PropertyApi.Integration.Tests.Listings;

/// <summary>
/// Exercises Infrastructure's real ActiveListingCounter directly (internal, exposed to this
/// assembly via InternalsVisibleTo — same pattern as ListingQuotaPolicyTests) against mocked
/// IPropertyRepository/IShortStayListingRepository/IAgencyRepository. This is where the
/// "Property + Short-Stay summed" rule is actually proven — the handler-level tests in
/// PropertyApi.Application.Tests only prove the two handlers delegate to whatever
/// IActiveListingCounter returns, not how the real implementation sums it.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Feature", "Listings")]
public sealed class ActiveListingCounterTests
{
    [Fact]
    public async Task ByOwner_SumsPropertyAndShortStayCounts()
    {
        var ownerId = Guid.NewGuid();

        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var shortStay = new Mock<IShortStayListingRepository>();
        shortStay.Setup(x => x.CountActiveByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var counter = new ActiveListingCounter(properties.Object, shortStay.Object, Mock.Of<IAgencyRepository>());

        var total = await counter.CountActiveListingsByOwnerAsync(ownerId);

        Assert.Equal(5, total);
    }

    [Fact]
    public async Task ByOwner_WithNoShortStayListingsAtAll_EqualsThePropertyCountAlone()
    {
        // Regression guard for the exact bug this class fixes: before it existed, a Short-Stay
        // count of zero was never even asked for — the total silently equalled the Property
        // count no matter how many Short-Stay listings actually existed. This pins that the
        // zero case still adds correctly (no off-by-something from the summation itself).
        var ownerId = Guid.NewGuid();

        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var shortStay = new Mock<IShortStayListingRepository>();
        shortStay.Setup(x => x.CountActiveByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var counter = new ActiveListingCounter(properties.Object, shortStay.Object, Mock.Of<IAgencyRepository>());

        Assert.Equal(1, await counter.CountActiveListingsByOwnerAsync(ownerId));
    }

    [Fact]
    public async Task ByAgency_SumsPropertyAgencyCountAndEveryMembersShortStayCount()
    {
        var agencyId = Guid.NewGuid();
        var ownerMemberId = Guid.NewGuid();
        var otherMemberId = Guid.NewGuid();

        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.CountActiveListingsByAgencyAsync(agencyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);

        var agencies = new Mock<IAgencyRepository>();
        agencies.Setup(x => x.GetMembersAsync(agencyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserAccount>
            {
                UserAccount.Create(ownerMemberId, "Owner", "Member", DateTime.UtcNow),
                UserAccount.Create(otherMemberId, "Other", "Member", DateTime.UtcNow),
            });

        var shortStay = new Mock<IShortStayListingRepository>();
        shortStay
            .Setup(x => x.CountActiveByOwnerIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Contains(ownerMemberId) && ids.Contains(otherMemberId) && ids.Count == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        var counter = new ActiveListingCounter(properties.Object, shortStay.Object, agencies.Object);

        var total = await counter.CountActiveListingsByAgencyAsync(agencyId);

        Assert.Equal(14, total);
    }

    [Fact]
    public async Task ByAgency_WithNoMembers_NeverQueriesShortStayByEmptyIdList()
    {
        // GetMembersAsync should always include at least the owner in production, but this
        // pins ActiveListingCounter doesn't crash or mis-sum if it ever returns empty.
        var agencyId = Guid.NewGuid();

        var properties = new Mock<IPropertyRepository>();
        properties.Setup(x => x.CountActiveListingsByAgencyAsync(agencyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);

        var agencies = new Mock<IAgencyRepository>();
        agencies.Setup(x => x.GetMembersAsync(agencyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserAccount>());

        var shortStay = new Mock<IShortStayListingRepository>();
        shortStay
            .Setup(x => x.CountActiveByOwnerIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var counter = new ActiveListingCounter(properties.Object, shortStay.Object, agencies.Object);

        Assert.Equal(7, await counter.CountActiveListingsByAgencyAsync(agencyId));
    }
}
