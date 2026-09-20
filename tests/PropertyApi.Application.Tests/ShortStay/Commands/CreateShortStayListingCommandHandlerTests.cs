using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.ShortStay.Commands.CreateShortStayListing;
using PropertyApi.Application.ShortStay.Interfaces;
using PropertyApi.Domain.ShortStay.Entities;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Tests.ShortStay.Commands;

/// <summary>
/// Covers the plan-quota bug fix: a Short-Stay listing must consume the SAME active-listing
/// pool a Property (Sale/Rent) listing does — see IActiveListingCounter and
/// CreateShortStayListingCommandHandler's doc comments. Mirrors
/// ListingLifecycleTests' CreateProperty quota tests in Moq style so the two listing types'
/// tests read the same way.
/// </summary>
public sealed class CreateShortStayListingCommandHandlerTests
{
    private static CreateShortStayListingCommand ValidCommand(Guid ownerId) => new(
        OwnerId: ownerId,
        AccommodationTypeId: 1,
        Title: "شاليه على البحر",
        Description: "وصف كافٍ للإعلان",
        Capacity: 4,
        Bedrooms: 2,
        Bathrooms: 1,
        CheckInTime: new TimeOnly(14, 0),
        CheckOutTime: new TimeOnly(11, 0),
        Latitude: 35.5m,
        Longitude: 35.8m,
        DefaultBasePricePerNight: 100m,
        PropertyId: null,
        CurrencyCode: "USD");

    private static Mock<IAccommodationTypeRepository> DefaultAccommodationTypes()
    {
        var repo = new Mock<IAccommodationTypeRepository>();
        repo.Setup(x => x.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccommodationType.Create("chalet", "شاليه", "Chalet", "Tourism"));
        return repo;
    }

    private static Mock<IAgencyRepository> DefaultAgencies()
    {
        var agencies = new Mock<IAgencyRepository>();
        agencies
            .Setup(x => x.GetUserAccountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid ownerId, CancellationToken _) => BuildAccountWithPlan(ownerId));
        return agencies;
    }

    private static Mock<IUserIdentityReadService> DefaultIdentity()
    {
        var identity = new Mock<IUserIdentityReadService>();
        identity
            .Setup(x => x.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid ownerId, CancellationToken _) => new IdentityAccountSnapshot(
                IdentityId: ownerId,
                UserAccountId: ownerId,
                Email: "owner@example.com",
                PhoneNumber: null,
                EmailConfirmed: true,
                PhoneConfirmed: false,
                HasPassword: true,
                IsDeleted: false));
        return identity;
    }

    private static UserAccount BuildAccountWithPlan(Guid userId)
    {
        var account = UserAccount.Create(userId, "نعيم", "بزازة", DateTime.UtcNow);
        account.SelectPlan(Guid.NewGuid(), DateTime.UtcNow);
        return account;
    }

    private static CreateShortStayListingCommandHandler MakeHandler(
        Mock<IShortStayListingRepository> listings,
        Mock<IActiveListingCounter> activeListingCounter,
        int limit,
        Mock<IAccommodationTypeRepository>? accommodationTypes = null,
        Mock<IAgencyRepository>? agencies = null,
        Mock<IUserIdentityReadService>? identity = null,
        Mock<IPropertyOwnershipService>? propertyOwnership = null)
        => new(
            listings.Object,
            (accommodationTypes ?? DefaultAccommodationTypes()).Object,
            (agencies ?? DefaultAgencies()).Object,
            (identity ?? DefaultIdentity()).Object,
            activeListingCounter.Object,
            new FakeListingQuotaPolicy(limit),
            (propertyOwnership ?? new Mock<IPropertyOwnershipService>()).Object,
            new Mock<IUnitOfWork>().Object,
            NullLogger<CreateShortStayListingCommandHandler>.Instance);


    [Fact]
    public async Task Handle_PropertyIdNotOwnedByCaller_IsRejectedBeforeAnythingIsPersisted()
    {
        var ownerId = Guid.NewGuid();
        var foreignPropertyId = Guid.NewGuid();
        var listings = new Mock<IShortStayListingRepository>();
        var counter = new Mock<IActiveListingCounter>();
        var ownership = new Mock<IPropertyOwnershipService>();
        ownership
            .Setup(x => x.EnsureOwnerAsync(foreignPropertyId, ownerId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ForbiddenException("not the owner"));

        var handler = MakeHandler(listings, counter, limit: 5, propertyOwnership: ownership);

        await Assert.ThrowsAsync<ForbiddenException>(
            () => handler.Handle(ValidCommand(ownerId) with { PropertyId = foreignPropertyId }, CancellationToken.None));

        listings.Verify(
            x => x.AddAsync(It.IsAny<ShortStayListing>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_PropertyIdOwnedByCaller_CreatesTheListing()
    {
        var ownerId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var listings = new Mock<IShortStayListingRepository>();
        var counter = new Mock<IActiveListingCounter>();
        counter.Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var ownership = new Mock<IPropertyOwnershipService>();

        var handler = MakeHandler(listings, counter, limit: 5, propertyOwnership: ownership);

        await handler.Handle(ValidCommand(ownerId) with { PropertyId = propertyId }, CancellationToken.None);

        ownership.Verify(x => x.EnsureOwnerAsync(propertyId, ownerId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        listings.Verify(x => x.AddAsync(It.IsAny<ShortStayListing>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null, "SYP")]   // client built before the currency field existed
    [InlineData("usd", "USD")]
    public async Task Handle_CurrencyOmittedOrGiven_ListingUsesDefaultOrGivenCurrency(string? sent, string expected)
    {
        var ownerId = Guid.NewGuid();
        var listings = new Mock<IShortStayListingRepository>();
        var counter = new Mock<IActiveListingCounter>();
        counter.Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        var handler = MakeHandler(listings, counter, limit: 1);

        var result = await handler.Handle(ValidCommand(ownerId) with { CurrencyCode = sent }, CancellationToken.None);

        Assert.Equal(expected, result.CurrencyCode);
    }

    [Fact]
    public async Task Handle_BelowTheUnifiedLimit_CreatesTheListing()
    {
        var ownerId = Guid.NewGuid();
        var listings = new Mock<IShortStayListingRepository>();
        var counter = new Mock<IActiveListingCounter>();
        counter.Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = MakeHandler(listings, counter, limit: 1);

        var result = await handler.Handle(ValidCommand(ownerId), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
        listings.Verify(x => x.AddAsync(It.IsAny<ShortStayListing>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AtTheUnifiedLimit_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var listings = new Mock<IShortStayListingRepository>();
        var counter = new Mock<IActiveListingCounter>();
        counter.Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = MakeHandler(listings, counter, limit: 1);

        await Assert.ThrowsAsync<ConflictException>(
            () => handler.Handle(ValidCommand(ownerId), CancellationToken.None));

        listings.Verify(
            x => x.AddAsync(It.IsAny<ShortStayListing>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// THE bug this fix targets: an owner already holding one active Property listing (the
    /// limit) must be blocked from creating a Short-Stay listing too — the two types share
    /// one pool. Before the fix, CreateShortStayListingCommandHandler never consulted this
    /// count at all, so this scenario always succeeded.
    /// </summary>
    [Fact]
    public async Task Handle_QuotaAlreadyConsumedByAPropertyListing_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var listings = new Mock<IShortStayListingRepository>();
        var counter = new Mock<IActiveListingCounter>();

        // The unified count (as ActiveListingCounter would report: Property + Short-Stay
        // summed) is already at the limit, even though this owner has zero Short-Stay
        // listings of their own.
        counter.Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = MakeHandler(listings, counter, limit: 1);

        await Assert.ThrowsAsync<ConflictException>(
            () => handler.Handle(ValidCommand(ownerId), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_OnElitePlan_WithManyActiveListings_IsStillAllowed()
    {
        var ownerId = Guid.NewGuid();
        var listings = new Mock<IShortStayListingRepository>();
        var counter = new Mock<IActiveListingCounter>();
        counter.Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10_000);

        var handler = MakeHandler(listings, counter, limit: int.MaxValue);

        var result = await handler.Handle(ValidCommand(ownerId), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
    }

    [Fact]
    public async Task Handle_AgencyPooledQuota_AtTheLimit_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var agencyId = Guid.NewGuid();

        var agencies = new Mock<IAgencyRepository>();
        agencies
            .Setup(x => x.GetUserAccountAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var account = BuildAccountWithPlan(ownerId);
                account.JoinAgency(agencyId, DateTime.UtcNow);
                return account;
            });

        var listings = new Mock<IShortStayListingRepository>();
        var counter = new Mock<IActiveListingCounter>();
        counter.Setup(x => x.CountActiveListingsByAgencyAsync(agencyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var handler = MakeHandler(listings, counter, limit: 1, agencies: agencies);

        await Assert.ThrowsAsync<ConflictException>(
            () => handler.Handle(ValidCommand(ownerId), CancellationToken.None));

        counter.Verify(
            x => x.CountActiveListingsByOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NoPlanSelected_IsForbiddenWithThePlanRequiredCode()
    {
        var ownerId = Guid.NewGuid();
        var agencies = new Mock<IAgencyRepository>();
        agencies
            .Setup(x => x.GetUserAccountAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserAccount.Create(ownerId, "نعيم", "بزازة", DateTime.UtcNow)); // no SelectPlan call

        var listings = new Mock<IShortStayListingRepository>();
        var counter = new Mock<IActiveListingCounter>();

        var handler = MakeHandler(listings, counter, limit: 50, agencies: agencies);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => handler.Handle(ValidCommand(ownerId), CancellationToken.None));

        Assert.Equal("LISTING_PLAN_REQUIRED", ex.Code);
        listings.Verify(x => x.AddAsync(It.IsAny<ShortStayListing>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ContactNotConfirmed_IsForbiddenWithTheContactRequiredCode()
    {
        var ownerId = Guid.NewGuid();
        var identity = new Mock<IUserIdentityReadService>();
        identity
            .Setup(x => x.FindByIdAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityAccountSnapshot(
                IdentityId: ownerId,
                UserAccountId: ownerId,
                Email: "owner@example.com",
                PhoneNumber: null,
                EmailConfirmed: false,
                PhoneConfirmed: false,
                HasPassword: true,
                IsDeleted: false));

        var listings = new Mock<IShortStayListingRepository>();
        var counter = new Mock<IActiveListingCounter>();

        var handler = MakeHandler(listings, counter, limit: 50, identity: identity);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => handler.Handle(ValidCommand(ownerId), CancellationToken.None));

        Assert.Equal("LISTING_CONTACT_NOT_CONFIRMED", ex.Code);
        listings.Verify(x => x.AddAsync(It.IsAny<ShortStayListing>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>Same deterministic stand-in ListingLifecycleTests uses for IListingQuotaPolicy.</summary>
    private sealed class FakeListingQuotaPolicy : IListingQuotaPolicy
    {
        private readonly int _limit;
        public FakeListingQuotaPolicy(int limit) => _limit = limit;

        public Task<int> GetActiveListingLimitAsync(UserAccount ownerAccount, CancellationToken ct = default)
            => Task.FromResult(_limit);
    }
}
