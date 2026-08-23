using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Commands.ConfirmListingExtensionPayment;
using PropertyApi.Application.Listings.Commands.CreateProperty;
using PropertyApi.Application.Listings.Commands.RequestListingExtension;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.Transactions.Entities;
using PropertyApi.Domain.Transactions.Enums;

namespace PropertyApi.Application.Tests.Listings;

/// <summary>
/// Covers the free-listing lifecycle: the quota that limits a free-tier owner to one
/// active listing, the domain transitions the expiry sweep drives, and the paid extension
/// that recovers an expired listing.
///
/// Weighted toward the destructive edges on purpose. ListingExpiryHostedService runs
/// unattended and ends in deleting listings, so the guards that stop it deleting the wrong
/// thing — and the guards that stop one payment buying two extensions — are the ones worth
/// pinning down.
/// </summary>
public sealed class ListingLifecycleTests
{
    // ── Domain: expiry transitions ───────────────────────────────

    [Fact]
    public void MarkExpired_WithoutExpiryDate_Throws()
    {
        var property = CreateListing();

        // A listing with no ExpiresAt was never on a clock. Expiring it would start a
        // deletion countdown for a listing that was never promised one.
        Assert.Throws<DomainException>(() => property.MarkExpired());
    }

    [Fact]
    public void MarkExpired_SetsExpiredStatusAndUnpublishes()
    {
        var property = CreateListing();
        property.ExpiresAt = DateTime.UtcNow.AddDays(-1);

        property.MarkExpired();

        Assert.Equal(PropertyStatus.Expired, property.Status);
        Assert.False(property.IsPublished);
        Assert.False(property.IsDeleted);
    }

    [Fact]
    public void SystemDelete_OnNonExpiredListing_Throws()
    {
        var property = CreateListing();
        property.ExpiresAt = DateTime.UtcNow.AddDays(30);

        // The sweep must never reach a live listing. If this guard ever goes,
        // a bad query predicate silently deletes published listings.
        Assert.Throws<DomainException>(() => property.MarkExpiredListingDeletedBySystem());
    }

    [Fact]
    public void SystemDelete_OnExpiredListing_SoftDeletesWithoutAttributingAUser()
    {
        var property = CreateListing();
        property.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        property.MarkExpired();

        property.MarkExpiredListingDeletedBySystem();

        Assert.True(property.IsDeleted);
        Assert.NotNull(property.DeletedAt);

        // No user deleted this; the scheduler did. Attributing it to a real user id would
        // put a deletion the owner never performed into their audit trail.
        Assert.Null(property.DeletedByUserId);
    }

    // ── Domain: extension ────────────────────────────────────────

    [Fact]
    public void ExtendPublication_MeasuresFromPaymentTime_NotFromOldExpiry()
    {
        var property = CreateListing();
        var expiredAt = DateTime.UtcNow.AddDays(-14);
        property.ExpiresAt = expiredAt;
        property.MarkExpired();

        var paidAt = DateTime.UtcNow;
        property.ExtendPublication(ListingLifecyclePolicy.PublicationPeriod, paidAt);

        // An owner who pays two weeks into the grace window gets a full period from the day
        // they paid — not a period already 14 days spent.
        Assert.Equal(paidAt.Add(ListingLifecyclePolicy.PublicationPeriod), property.ExpiresAt);
        Assert.Equal(PropertyStatus.Available, property.Status);
        Assert.True(property.IsPublished);
    }

    [Fact]
    public void ExtendPublication_ClearsWarningStamp_SoTheNextWindowWarnsAgain()
    {
        var property = CreateListing();
        property.ExpiresAt = DateTime.UtcNow.AddDays(3);
        property.MarkExpiryWarningSent();
        Assert.NotNull(property.ExpiryWarningSentAt);

        property.ExtendPublication(ListingLifecyclePolicy.PublicationPeriod, DateTime.UtcNow);

        Assert.Null(property.ExpiryWarningSentAt);
    }

    [Fact]
    public void ExtendPublication_WithNonPositivePeriod_Throws()
    {
        var property = CreateListing();

        Assert.Throws<DomainException>(
            () => property.ExtendPublication(TimeSpan.Zero, DateTime.UtcNow));
    }

    // ── Application: free-tier quota ─────────────────────────────

    [Fact]
    public async Task CreateProperty_AtFreeTierLimit_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var repository = new Mock<IPropertyRepository>();
        repository
            .Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ListingLifecyclePolicy.FreeTierActiveListingLimit);

        var handler = CreatePropertyHandler(repository);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId },
            CancellationToken.None));

        // Rejected before anything was written.
        repository.Verify(
            x => x.AddAsync(It.IsAny<Property>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateProperty_BelowFreeTierLimit_IsAllowed()
    {
        var ownerId = Guid.NewGuid();
        var repository = new Mock<IPropertyRepository>();
        repository
            .Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ListingLifecyclePolicy.FreeTierActiveListingLimit - 1);

        var handler = CreatePropertyHandler(repository);

        var id = await handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId },
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
        repository.Verify(
            x => x.AddAsync(It.IsAny<Property>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Application: extension request ───────────────────────────

    [Fact]
    public async Task RequestExtension_OnListingStillInsideItsWindow_IsRejected()
    {
        var property = CreateListing();
        property.ExpiresAt = DateTime.UtcNow.AddDays(60);

        var handler = RequestExtensionHandler(property, existingPendingFee: null, out _);

        // Taking money to extend a listing that has 60 days left would be charging for
        // nothing.
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new RequestListingExtensionCommand(property.Id, property.OwnerId),
            CancellationToken.None));
    }

    [Fact]
    public async Task RequestExtension_ByNonOwner_IsRejected()
    {
        var property = CreateListing();
        property.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        property.MarkExpired();

        var handler = RequestExtensionHandler(property, existingPendingFee: null, out _);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new RequestListingExtensionCommand(property.Id, Guid.NewGuid()),
            CancellationToken.None));
    }

    [Fact]
    public async Task RequestExtension_AfterGraceWindow_IsRejected()
    {
        var property = CreateListing();
        property.ExpiresAt = DateTime.UtcNow
            .Subtract(ListingLifecyclePolicy.GracePeriodBeforeDeletion)
            .AddDays(-1);
        property.MarkExpired();

        var handler = RequestExtensionHandler(property, existingPendingFee: null, out _);

        // Past the grace window the listing cannot come back, so the API must not sell an
        // extension for it.
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new RequestListingExtensionCommand(property.Id, property.OwnerId),
            CancellationToken.None));
    }

    [Fact]
    public async Task RequestExtension_Twice_DoesNotStackCharges()
    {
        var property = CreateListing();
        property.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        property.MarkExpired();

        var pending = BuildPendingFee(property.Id, property.OwnerId);
        var handler = RequestExtensionHandler(property, pending, out var extensions);

        var quote = await handler.Handle(
            new RequestListingExtensionCommand(property.Id, property.OwnerId),
            CancellationToken.None);

        Assert.True(quote.IsExistingPendingFee);
        Assert.Equal(pending.Id, quote.TransactionId);

        // Tapping "extend" repeatedly must leave the owner owing one dollar, not three.
        extensions.Verify(
            x => x.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Application: extension confirmation ──────────────────────

    [Fact]
    public async Task ConfirmExtension_OnAlreadyCompletedFee_IsRejected()
    {
        var property = CreateListing();
        property.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        property.MarkExpired();

        var fee = BuildPendingFee(property.Id, property.OwnerId);
        fee.MarkCompleted();

        var handler = ConfirmExtensionHandler(property, fee);

        // Replaying a confirmation must not buy a second publication period.
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new ConfirmListingExtensionPaymentCommand(fee.Id, Guid.NewGuid()),
            CancellationToken.None));
    }

    [Fact]
    public async Task ConfirmExtension_OnPendingFee_SettlesItAndRestoresTheListing()
    {
        var property = CreateListing();
        property.ExpiresAt = DateTime.UtcNow.AddDays(-2);
        property.MarkExpired();

        var fee = BuildPendingFee(property.Id, property.OwnerId);
        var handler = ConfirmExtensionHandler(property, fee);

        var newExpiresAt = await handler.Handle(
            new ConfirmListingExtensionPaymentCommand(fee.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(TransactionStatus.Completed, fee.Status);
        Assert.Equal(PropertyStatus.Available, property.Status);
        Assert.True(property.IsPublished);
        Assert.True(newExpiresAt > DateTime.UtcNow.AddDays(80));
    }

    [Fact]
    public async Task ConfirmExtension_OnAFeeThatIsNotAnExtension_IsRejected()
    {
        var property = CreateListing();
        property.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        property.MarkExpired();

        var commission = Transaction.Create(
            propertyId: property.Id,
            payerId: property.OwnerId,
            receiverId: Guid.NewGuid(),
            transactionType: TransactionType.CommissionFee,
            amount: 500m,
            currencyId: 1,
            exchangeRateToUSD: 1m,
            paymentMethod: TransactionPaymentMethod.BankTransfer);

        var handler = ConfirmExtensionHandler(property, commission);

        // Confirming an unrelated payment must not hand out a free extension.
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new ConfirmListingExtensionPaymentCommand(commission.Id, Guid.NewGuid()),
            CancellationToken.None));
    }

    // ── Fixtures ─────────────────────────────────────────────────

    private static Property CreateListing() => Property.Create(
        "شقة للإيجار",
        "وصف كافٍ للإعلان",
        Guid.NewGuid(),
        ListingType.ForRent);

    private static Transaction BuildPendingFee(Guid propertyId, Guid ownerId) => Transaction.Create(
        propertyId: propertyId,
        payerId: ownerId,
        receiverId: Guid.Empty,
        transactionType: TransactionType.ListingExtensionFee,
        amount: ListingLifecyclePolicy.SingleListingExtensionFeeUsd,
        currencyId: 1,
        exchangeRateToUSD: 1m,
        paymentMethod: TransactionPaymentMethod.BankTransfer);

    private static CreatePropertyCommandHandler CreatePropertyHandler(Mock<IPropertyRepository> repository)
        => new(
            repository.Object,
            Mock.Of<IAgencyRepository>(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<ILocationSuggestionService>(),
            NullLogger<CreatePropertyCommandHandler>.Instance);

    private static RequestListingExtensionCommandHandler RequestExtensionHandler(
        Property property,
        Transaction? existingPendingFee,
        out Mock<IListingFeeRepository> extensions)
    {
        var properties = new Mock<IPropertyRepository>();
        properties
            .Setup(x => x.GetByIdAsync(property.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);

        extensions = new Mock<IListingFeeRepository>();
        extensions
            .Setup(x => x.GetPendingFeeAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<TransactionType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPendingFee);
        extensions
            .Setup(x => x.GetUsdCurrencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((1, 1m));

        return new RequestListingExtensionCommandHandler(
            properties.Object,
            extensions.Object,
            Mock.Of<IUnitOfWork>(),
            NullLogger<RequestListingExtensionCommandHandler>.Instance);
    }

    private static ConfirmListingExtensionPaymentCommandHandler ConfirmExtensionHandler(
        Property property,
        Transaction fee)
    {
        var extensions = new Mock<IListingFeeRepository>();
        extensions
            .Setup(x => x.GetByIdAsync(fee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fee);

        var properties = new Mock<IPropertyRepository>();
        properties
            .Setup(x => x.GetByIdAsync(property.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);

        return new ConfirmListingExtensionPaymentCommandHandler(
            extensions.Object,
            properties.Object,
            Mock.Of<IUnitOfWork>(),
            NullLogger<ConfirmListingExtensionPaymentCommandHandler>.Instance);
    }

    private static CreatePropertyCommand ValidCreateCommand() => new(
        OwnerId: Guid.NewGuid(),
        Title: "شقة فاخرة",
        Description: "وصف كافٍ للإعلان",
        ListingType: ListingType.ForRent,
        Street: "شارع الثورة",
        City: "دمشق",
        Region: null,
        CountryCode: "SY",
        PostalCode: null,
        GovernorateId: 1,
        DistrictId: 1,
        DistrictText: null,
        NeighborhoodId: null,
        NeighborhoodText: null,
        PropertyTypeId: 1,
        Latitude: null,
        Longitude: null,
        ColdRent: 300m,
        WarmRent: null,
        PurchasePrice: null,
        Deposit: null,
        AdditionalCosts: null,
        CurrencyCode: "USD",
        Rooms: 3,
        Area: 90m,
        Floor: 2,
        HasBalcony: true,
        HasElevator: false,
        HasParkingSpace: false,
        HeatingType: HeatingType.Central,
        AvailableFrom: null,
        AmenityIds: null);
}
