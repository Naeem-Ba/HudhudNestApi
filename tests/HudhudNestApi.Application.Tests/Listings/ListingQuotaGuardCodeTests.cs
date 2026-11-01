using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Listings.Services;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Tests.Listings;

/// <summary>QA Phase 16 D04: the quota refusal carries a stable code so the client can show it in the reader's language.</summary>
public sealed class ListingQuotaGuardCodeTests
{
    private static ListingQuotaGuard Guard(int limit, int active)
    {
        var policy = new Mock<IListingQuotaPolicy>();
        policy.Setup(x => x.GetActiveListingLimitAsync(It.IsAny<UserAccount>(), It.IsAny<CancellationToken>())).ReturnsAsync(limit);
        var counter = new Mock<IActiveListingCounter>();
        counter.Setup(x => x.CountActiveListingsByOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(active);
        return new ListingQuotaGuard(policy.Object, counter.Object, NullLogger<ListingQuotaGuard>.Instance);
    }

    [Fact]
    public async Task ReachedQuota_ThrowsAConflictWithTheStableCode()
    {
        var owner = UserAccount.Create(Guid.NewGuid(), "Qa", "Owner", DateTime.UtcNow);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => Guard(limit: 1, active: 1).EnsureCapacityAsync(owner));

        Assert.Equal("LISTING_QUOTA_REACHED", ex.Code);
        Assert.NotEmpty(ex.Message);
    }

    [Fact]
    public async Task BelowTheQuota_DoesNotThrow()
        => await Guard(limit: 2, active: 1).EnsureCapacityAsync(UserAccount.Create(Guid.NewGuid(), "Qa", "Owner", DateTime.UtcNow));
}
