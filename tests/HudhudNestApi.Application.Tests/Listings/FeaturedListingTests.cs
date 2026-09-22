using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Commands.ConfirmFeaturedListingPayment;
using HudhudNestApi.Application.Listings.Commands.RequestFeaturedListing;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.Listings.Mappers;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Enums;
using HudhudNestApi.Domain.Listings;
using HudhudNestApi.Domain.Listings.Entities;
using HudhudNestApi.Domain.Transactions.Entities;
using HudhudNestApi.Domain.Transactions.Enums;

namespace HudhudNestApi.Application.Tests.Listings;

/// <summary>
/// Covers the paid featured-placement path.
///
/// Weighted toward the money, like the extension tests: the cases that matter are the ones
/// where a listing could get promoted without a settled payment, where one payment could
/// buy two placements, or where an owner could be charged for days they already own.
/// </summary>
public sealed class FeaturedListingTests
{
    // ── Domain: MarkFeatured ─────────────────────────────────────

    [Fact]
    public void MarkFeatured_OnUnfeaturedListing_RunsFromNow()
    {
        var property = CreatePublishedListing();
        var now = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        property.MarkFeatured(TimeSpan.FromDays(30), now);

        Assert.True(property.IsFeatured);
        Assert.Equal(now.AddDays(30), property.FeaturedUntil);
    }

    [Fact]
    public void MarkFeatured_WhileStillFeatured_AddsToRemainingTime()
    {
        var property = CreatePublishedListing();
        var start = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        property.MarkFeatured(TimeSpan.FromDays(30), start);

        // Buying again 10 days in must not throw away the 20 days already paid for.
        var secondPurchase = start.AddDays(10);
        property.MarkFeatured(TimeSpan.FromDays(30), secondPurchase);

        Assert.Equal(start.AddDays(60), property.FeaturedUntil);
    }

    [Fact]
    public void MarkFeatured_AfterPreviousPlacementLapsed_RunsFromNowNotFromOldExpiry()
    {
        var property = CreatePublishedListing();
        var start = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        property.MarkFeatured(TimeSpan.FromDays(30), start);
        property.ClearFeatured();

        // Two months later the old window is long gone; the new one must not be backdated
        // into the past, which would deliver a placement that had already elapsed.
        var muchLater = start.AddDays(60);
        property.MarkFeatured(TimeSpan.FromDays(30), muchLater);

        Assert.Equal(muchLater.AddDays(30), property.FeaturedUntil);
    }

    [Fact]
    public void MarkFeatured_OnExpiredListing_Throws()
    {
        var property = CreatePublishedListing();
        property.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        property.MarkExpired();

        // Selling promotion for a listing hidden from search is selling nothing.
        Assert.Throws<DomainException>(
            () => property.MarkFeatured(TimeSpan.FromDays(30), DateTime.UtcNow));
    }

    [Fact]
    public void MarkFeatured_OnDeletedListing_Throws()
    {
        var property = CreatePublishedListing();
        property.IsDeleted = true;

        Assert.Throws<DomainException>(
            () => property.MarkFeatured(TimeSpan.FromDays(30), DateTime.UtcNow));
    }

    [Fact]
    public void ClearFeatured_KeepsFeaturedUntilAsTheRecordOfWhatWasPaidFor()
    {
        var property = CreatePublishedListing();
        var start = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        property.MarkFeatured(TimeSpan.FromDays(30), start);
        property.ClearFeatured();

        Assert.False(property.IsFeatured);
        Assert.Equal(start.AddDays(30), property.FeaturedUntil);
    }

    [Fact]
    public void IsCurrentlyFeatured_IsFalseOnceTheWindowElapsed_EvenBeforeTheSweepRuns()
    {
        var property = CreatePublishedListing();
        var start = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        property.MarkFeatured(TimeSpan.FromDays(30), start);

        // The flag is still true — the sweep has not run yet — but read paths must already
        // treat the placement as over.
        Assert.True(property.IsFeatured);
        Assert.False(property.IsCurrentlyFeatured(start.AddDays(31)));
        Assert.True(property.IsCurrentlyFeatured(start.AddDays(29)));
    }

    // ── Request: records a charge, promotes nothing ──────────────

    [Fact]
    public async Task RequestFeatured_RecordsPendingFeeAndDoesNotPromote()
    {
        var property = CreatePublishedListing();
        var handler = RequestHandler(property, existingPendingFee: null, out var fees);

        var quote = await handler.Handle(
            new RequestFeaturedListingCommand(property.Id, property.OwnerId),
            CancellationToken.None);

        Assert.False(property.IsFeatured);
        Assert.Equal(ListingLifecyclePolicy.FeaturedListingFeeUsd, quote.AmountUsd);
        Assert.False(quote.IsExistingPendingFee);
        fees.Verify(
            x => x.AddAsync(
                It.Is<Transaction>(t =>
                    t.TransactionType == TransactionType.FeaturedListingFee &&
                    t.Status == TransactionStatus.Pending),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RequestFeatured_Twice_ReusesTheSameChargeInsteadOfStackingOne()
    {
        var property = CreatePublishedListing();
        var pending = BuildPendingFeaturedFee(property.Id, property.OwnerId);
        var handler = RequestHandler(property, pending, out var fees);

        var quote = await handler.Handle(
            new RequestFeaturedListingCommand(property.Id, property.OwnerId),
            CancellationToken.None);

        Assert.True(quote.IsExistingPendingFee);
        Assert.Equal(pending.Id, quote.TransactionId);
        fees.Verify(
            x => x.AddAsync(It.IsAny<Transaction>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RequestFeatured_ByNonOwner_IsForbidden()
    {
        var property = CreatePublishedListing();
        var handler = RequestHandler(property, existingPendingFee: null, out _);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new RequestFeaturedListingCommand(property.Id, Guid.NewGuid()),
            CancellationToken.None));
    }

    [Fact]
    public async Task RequestFeatured_OnExpiredListing_IsRejected()
    {
        var property = CreatePublishedListing();
        property.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        property.MarkExpired();

        var handler = RequestHandler(property, existingPendingFee: null, out _);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new RequestFeaturedListingCommand(property.Id, property.OwnerId),
            CancellationToken.None));
    }

    [Fact]
    public async Task RequestFeatured_ProjectsFromExistingPlacementNotFromNow()
    {
        var property = CreatePublishedListing();
        property.MarkFeatured(TimeSpan.FromDays(30), DateTime.UtcNow);
        var currentUntil = property.FeaturedUntil!.Value;

        var handler = RequestHandler(property, existingPendingFee: null, out _);

        var quote = await handler.Handle(
            new RequestFeaturedListingCommand(property.Id, property.OwnerId),
            CancellationToken.None);

        // The owner must be told they are buying days on top of what they hold, not that
        // their remaining days are being replaced.
        Assert.Equal(currentUntil, quote.CurrentFeaturedUntil);
        Assert.Equal(
            currentUntil.Add(ListingLifecyclePolicy.FeaturedPeriod),
            quote.ProjectedFeaturedUntil);
    }

    // ── Confirm: the only place promotion happens ────────────────

    [Fact]
    public async Task ConfirmFeaturedPayment_SettlesFeeAndPromotesListing()
    {
        var property = CreatePublishedListing();
        var fee = BuildPendingFeaturedFee(property.Id, property.OwnerId);
        var handler = ConfirmHandler(property, fee);

        var featuredUntil = await handler.Handle(
            new ConfirmFeaturedListingPaymentCommand(fee.Id, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(TransactionStatus.Completed, fee.Status);
        Assert.True(property.IsFeatured);
        Assert.Equal(property.FeaturedUntil, featuredUntil);
    }

    [Fact]
    public async Task ConfirmFeaturedPayment_OnAlreadyCompletedFee_IsRejected()
    {
        var property = CreatePublishedListing();
        var fee = BuildPendingFeaturedFee(property.Id, property.OwnerId);
        fee.MarkCompleted();

        var handler = ConfirmHandler(property, fee);

        // Replaying a confirmation must not buy a second placement with one payment.
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new ConfirmFeaturedListingPaymentCommand(fee.Id, Guid.NewGuid()),
            CancellationToken.None));

        Assert.False(property.IsFeatured);
    }

    [Fact]
    public async Task ConfirmFeaturedPayment_OnAnExtensionFee_IsRejected()
    {
        var property = CreatePublishedListing();

        // Same shape, different product and a fifth of the price. Confirming one as the
        // other would hand out a $5 placement for $1.
        var extensionFee = Transaction.Create(
            propertyId: property.Id,
            payerId: property.OwnerId,
            receiverId: Guid.Empty,
            transactionType: TransactionType.ListingExtensionFee,
            amount: ListingLifecyclePolicy.SingleListingExtensionFeeUsd,
            currencyId: 1,
            exchangeRateToUSD: 1m,
            paymentMethod: TransactionPaymentMethod.BankTransfer);

        var handler = ConfirmHandler(property, extensionFee);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new ConfirmFeaturedListingPaymentCommand(extensionFee.Id, Guid.NewGuid()),
            CancellationToken.None));

        Assert.False(property.IsFeatured);
    }

    // ── Helpers ──────────────────────────────────────────────────

    // ── Delivery: the placement has to be visible and to rank ────
    //
    // Everything above proves the money is handled correctly. These prove the buyer gets
    // something for it. Before this, IsFeatured was written by the payment path and read by
    // nothing at all — no query, no sort, no DTO — so a paid placement changed nothing a
    // single user could see.

    [Fact]
    public void CurrentlyFeaturedExpression_AgreesWithDomainMethod_InEveryReachableState()
    {
        var now = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var predicate = Property.CurrentlyFeatured(now).Compile();

        var neverFeatured = CreatePublishedListing();

        var activelyFeatured = CreatePublishedListing();
        activelyFeatured.MarkFeatured(TimeSpan.FromDays(30), now.AddDays(-1));

        // Paid window elapsed, flag not yet cleared — the sweep runs every six hours, so this
        // is a state real rows sit in, not a hypothetical.
        var lapsedButStillFlagged = CreatePublishedListing();
        lapsedButStillFlagged.MarkFeatured(TimeSpan.FromDays(30), now.AddDays(-31));

        var sweptClear = CreatePublishedListing();
        sweptClear.MarkFeatured(TimeSpan.FromDays(30), now.AddDays(-31));
        sweptClear.ClearFeatured();

        Property[] states =
            [neverFeatured, activelyFeatured, lapsedButStillFlagged, sweptClear];

        foreach (var property in states)
        {
            Assert.Equal(property.IsCurrentlyFeatured(now), predicate(property));
        }

        // And the expression must actually discriminate — an expression that returned false
        // for everything would satisfy the agreement check above without doing anything.
        Assert.True(predicate(activelyFeatured));
        Assert.False(predicate(neverFeatured));
        Assert.False(predicate(lapsedButStillFlagged));
        Assert.False(predicate(sweptClear));
    }

    [Fact]
    public void Dto_ReportsActiveFeaturedPlacement()
    {
        var now = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var property = CreatePublishedListing();
        property.MarkFeatured(TimeSpan.FromDays(30), now);

        var dto = PropertyMapper.ToDto(property, now.AddDays(1));

        Assert.True(dto.IsFeatured);
        Assert.Equal(now.AddDays(30), dto.FeaturedUntil);
    }

    [Fact]
    public void Dto_DoesNotReportFeatured_OnceThePaidWindowHasElapsed()
    {
        var start = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var property = CreatePublishedListing();
        property.MarkFeatured(TimeSpan.FromDays(30), start);

        // One minute past expiry, hours before the sweep would clear the column.
        var dto = PropertyMapper.ToDto(property, start.AddDays(30).AddMinutes(1));

        Assert.True(property.IsFeatured);   // the raw column still says yes
        Assert.False(dto.IsFeatured);       // what the client is told does not
        Assert.Equal(start.AddDays(30), dto.FeaturedUntil);
    }

    private static Property CreatePublishedListing()
    {
        var property = Property.Create(
            "شقة للإيجار",
            "وصف كافٍ للإعلان",
            Guid.NewGuid(),
            ListingType.ForRent);

        return property;
    }

    private static Transaction BuildPendingFeaturedFee(Guid propertyId, Guid ownerId)
        => Transaction.Create(
            propertyId: propertyId,
            payerId: ownerId,
            receiverId: Guid.Empty,
            transactionType: TransactionType.FeaturedListingFee,
            amount: ListingLifecyclePolicy.FeaturedListingFeeUsd,
            currencyId: 1,
            exchangeRateToUSD: 1m,
            paymentMethod: TransactionPaymentMethod.BankTransfer);

    private static RequestFeaturedListingCommandHandler RequestHandler(
        Property property,
        Transaction? existingPendingFee,
        out Mock<IListingFeeRepository> fees)
    {
        var properties = new Mock<IPropertyRepository>();
        properties
            .Setup(x => x.GetByIdAsync(property.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);

        fees = new Mock<IListingFeeRepository>();
        fees
            .Setup(x => x.GetPendingFeeAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<TransactionType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPendingFee);
        fees
            .Setup(x => x.GetUsdCurrencyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((1, 1m));

        return new RequestFeaturedListingCommandHandler(
            properties.Object,
            fees.Object,
            Mock.Of<IUnitOfWork>(),
            NullLogger<RequestFeaturedListingCommandHandler>.Instance);
    }

    private static ConfirmFeaturedListingPaymentCommandHandler ConfirmHandler(
        Property property,
        Transaction fee)
    {
        var fees = new Mock<IListingFeeRepository>();
        fees
            .Setup(x => x.GetByIdAsync(fee.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fee);

        var properties = new Mock<IPropertyRepository>();
        properties
            .Setup(x => x.GetByIdAsync(property.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);

        return new ConfirmFeaturedListingPaymentCommandHandler(
            fees.Object,
            properties.Object,
            Mock.Of<IUnitOfWork>(),
            NullLogger<ConfirmFeaturedListingPaymentCommandHandler>.Instance);
    }
}
