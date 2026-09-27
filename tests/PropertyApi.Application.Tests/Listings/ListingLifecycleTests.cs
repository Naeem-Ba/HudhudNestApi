using Microsoft.Extensions.Logging.Abstractions;
using MediatR;
using Moq;
using PropertyApi.Application.Agencies.Interfaces;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings;
using PropertyApi.Application.Listings.Commands.ConfirmListingExtensionPayment;
using PropertyApi.Application.Listings.Commands.CreateProperty;
using PropertyApi.Application.Listings.Commands.RequestListingExtension;
using PropertyApi.Application.Listings.Events;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.Listings.Mappers;
using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Enums;
using PropertyApi.Domain.Listings;
using PropertyApi.Domain.Listings.Entities;
using PropertyApi.Domain.Transactions.Entities;
using PropertyApi.Domain.Transactions.Enums;
using PropertyApi.Domain.Users.Entities;

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

    // ── Application: plan-driven quota, individual owner (BACKEND-ISSUES.md §B-3) ──

    [Fact]
    public async Task CreateProperty_OnFreePlan_AtTheLimit_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var repository = new Mock<IPropertyRepository>();
        repository
            .Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Free plan's real ListingLimit (seeded as 1) — not a constant in this handler
        // anymore, so the test drives it through the policy exactly as production does.
        var handler = CreatePropertyHandler(repository, quotaPolicy: new FakeListingQuotaPolicy(1));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId },
            CancellationToken.None));

        // Rejected before anything was written.
        repository.Verify(
            x => x.AddAsync(It.IsAny<Property>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateProperty_OnFreePlan_BelowTheLimit_IsAllowed()
    {
        var ownerId = Guid.NewGuid();
        var repository = new Mock<IPropertyRepository>();
        repository
            .Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var handler = CreatePropertyHandler(repository, quotaPolicy: new FakeListingQuotaPolicy(1));

        var id = await handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId },
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
        repository.Verify(
            x => x.AddAsync(It.IsAny<Property>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateProperty_OnPremiumPlan_UsesThePlanLimit_NotTheOldFreeTierConstant()
    {
        // A Premium owner sitting on 5 active listings must NOT be rejected by the retired
        // free-tier constant (1) — only Plan.ListingLimit (250, mirrored here by the fake)
        // governs now.
        var ownerId = Guid.NewGuid();
        var repository = new Mock<IPropertyRepository>();
        repository
            .Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var handler = CreatePropertyHandler(repository, quotaPolicy: new FakeListingQuotaPolicy(250));

        var id = await handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId },
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task CreateProperty_OnPremiumPlan_AtThePlanLimit_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var repository = new Mock<IPropertyRepository>();
        repository
            .Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(250);

        var handler = CreatePropertyHandler(repository, quotaPolicy: new FakeListingQuotaPolicy(250));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId },
            CancellationToken.None));
    }

    [Fact]
    public async Task CreateProperty_PublishesPropertyPublishedEvent_AfterTheListingIsCommitted()
    {
        // A listing created through the app is live immediately (Property.Create defaults to
        // published), so this is the moment SocialDistribution must hear about it — before this
        // event existed, only the explicit PATCH /publish (drafts) ever triggered distribution.
        var ownerId = Guid.NewGuid();
        var repository = new Mock<IPropertyRepository>();
        var uow = new Mock<IUnitOfWork>();
        var publisher = new Mock<IPublisher>();
        var order = new List<string>();
        uow.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>())).Callback(() => order.Add("commit"));
        publisher
            .Setup(x => x.Publish(It.IsAny<PropertyPublishedEvent>(), It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("publish"))
            .Returns(Task.CompletedTask);

        var handler = CreatePropertyHandler(repository, unitOfWork: uow, publisher: publisher);

        var id = await handler.Handle(ValidCreateCommand() with { OwnerId = ownerId }, CancellationToken.None);

        publisher.Verify(
            x => x.Publish(
                It.Is<PropertyPublishedEvent>(e => e.PropertyId == id && e.PublishedByUserId == ownerId),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(new[] { "commit", "publish" }, order);
    }

    [Fact]
    public async Task CreateProperty_RejectedByQuota_PublishesNoEvent()
    {
        var ownerId = Guid.NewGuid();
        var repository = new Mock<IPropertyRepository>();
        repository
            .Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(250);
        var publisher = new Mock<IPublisher>();

        var handler = CreatePropertyHandler(repository, quotaPolicy: new FakeListingQuotaPolicy(250), publisher: publisher);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId }, CancellationToken.None));

        publisher.Verify(x => x.Publish(It.IsAny<PropertyPublishedEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateProperty_WhenTheEventPublishThrows_StillReturnsTheNewListing()
    {
        // Distribution is a side effect: even a broken subscriber pipeline must never turn a
        // successfully saved listing into a failed request the owner would then retry (creating a duplicate).
        var publisher = new Mock<IPublisher>();
        publisher
            .Setup(x => x.Publish(It.IsAny<PropertyPublishedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("subscriber blew up"));

        var handler = CreatePropertyHandler(new Mock<IPropertyRepository>(), publisher: publisher);

        var id = await handler.Handle(ValidCreateCommand(), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task CreateProperty_OnElitePlan_WithManyActiveListings_IsStillAllowed()
    {
        // Elite's unlimited quota (Plan.ListingLimit == null) is represented by
        // IListingQuotaPolicy as int.MaxValue — a very large existing count must still not
        // be rejected.
        var ownerId = Guid.NewGuid();
        var repository = new Mock<IPropertyRepository>();
        repository
            .Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10_000);

        var handler = CreatePropertyHandler(
            repository, quotaPolicy: new FakeListingQuotaPolicy(int.MaxValue));

        var id = await handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId },
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task CreateProperty_AsksTheQuotaPolicy_ForTheSameAccountItLoadedUpFront()
    {
        // The handler must not re-derive or substitute a different account when consulting
        // the quota policy — it is the same UserAccount read once at the top of Handle().
        var ownerId = Guid.NewGuid();
        var repository = new Mock<IPropertyRepository>();
        repository
            .Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var agencies = new Mock<IAgencyRepository>();
        var account = BuildAccountWithPlan(ownerId);
        agencies
            .Setup(x => x.GetUserAccountAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var quotaPolicy = new FakeListingQuotaPolicy(1);
        var handler = CreatePropertyHandler(repository, agencies, quotaPolicy);

        await handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId },
            CancellationToken.None);

        Assert.Same(account, quotaPolicy.LastQueriedAccount);
    }

    // ── Application: agency-pooled quota (RELEASE-BLOCKERS-AR.md B-3) ─────

    [Fact]
    public async Task CreateProperty_ForAgencyMemberAtAgencyLimit_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var agencyId = Guid.NewGuid();

        var agencies = new Mock<IAgencyRepository>();
        agencies
            .Setup(x => x.GetUserAccountAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildAccountInAgency(ownerId, agencyId));

        var repository = new Mock<IPropertyRepository>();
        repository
            .Setup(x => x.CountActiveListingsByAgencyAsync(agencyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(50);

        var handler = CreatePropertyHandler(repository, agencies, new FakeListingQuotaPolicy(50));

        // The individual-owner count path must NOT apply here — only the agency-pooled
        // count does.
        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId },
            CancellationToken.None));

        repository.Verify(
            x => x.CountActiveListingsByOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        repository.Verify(
            x => x.AddAsync(It.IsAny<Property>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateProperty_ForAgencyMemberBelowAgencyLimit_IsAllowed()
    {
        var ownerId = Guid.NewGuid();
        var agencyId = Guid.NewGuid();

        var agencies = new Mock<IAgencyRepository>();
        agencies
            .Setup(x => x.GetUserAccountAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildAccountInAgency(ownerId, agencyId));

        var repository = new Mock<IPropertyRepository>();
        repository
            .Setup(x => x.CountActiveListingsByAgencyAsync(agencyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(49);

        var handler = CreatePropertyHandler(repository, agencies, new FakeListingQuotaPolicy(50));

        // This member individually already has zero listings of their own — the point is
        // that a member with plenty of room left may still be blocked once the agency's
        // shared pool (checked here, not the member's own count) is full, and is not
        // blocked purely for holding "their share": the pool has no per-head share at all.
        var id = await handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId },
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
        repository.Verify(
            x => x.AddAsync(It.IsAny<Property>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateProperty_ForAgencyMember_AsksTheQuotaPolicy_ForTheMembersOwnAccount()
    {
        // The handler's job stops at "hand the policy the account it loaded" — it must NOT
        // try to resolve the agency owner's plan itself (that would put a Plan-loading
        // responsibility directly in CreatePropertyCommandHandler, which the architecture
        // deliberately keeps out of it — see IListingQuotaPolicy's doc comment). Whether the
        // limit actually comes from the *owner's* plan rather than this member's own is
        // ListingQuotaPolicy's job, covered separately in
        // PropertyApi.Integration.Tests/Listings/ListingQuotaPolicyTests.cs.
        var ownerId = Guid.NewGuid();
        var agencyId = Guid.NewGuid();

        var agencies = new Mock<IAgencyRepository>();
        var member = BuildAccountInAgency(ownerId, agencyId);
        agencies
            .Setup(x => x.GetUserAccountAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(member);

        var repository = new Mock<IPropertyRepository>();
        repository
            .Setup(x => x.CountActiveListingsByAgencyAsync(agencyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var quotaPolicy = new FakeListingQuotaPolicy(50);
        var handler = CreatePropertyHandler(repository, agencies, quotaPolicy);

        await handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId },
            CancellationToken.None);

        Assert.Same(member, quotaPolicy.LastQueriedAccount);
    }

    // ── Application: quota check race is closed by an advisory lock (RELEASE-BLOCKERS-AR.md B-10) ──

    [Fact]
    public async Task CreateProperty_OnSuccess_AcquiresTheOwnerLockInsideATransactionThenCommits()
    {
        var ownerId = Guid.NewGuid();
        var repository = new Mock<IPropertyRepository>();
        repository
            .Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var uow = new Mock<IUnitOfWork>();
        var handler = CreatePropertyHandler(repository, unitOfWork: uow);

        await handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId },
            CancellationToken.None);

        var expectedKey = ListingQuotaLock.ForOwner(ownerId);

        uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.AcquireAdvisoryLockAsync(expectedKey, It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateProperty_ForAgencyMember_AcquiresTheAgencyLock_NotTheOwnerLock()
    {
        var ownerId = Guid.NewGuid();
        var agencyId = Guid.NewGuid();

        var agencies = new Mock<IAgencyRepository>();
        agencies
            .Setup(x => x.GetUserAccountAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildAccountInAgency(ownerId, agencyId));

        var repository = new Mock<IPropertyRepository>();
        repository
            .Setup(x => x.CountActiveListingsByAgencyAsync(agencyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var uow = new Mock<IUnitOfWork>();
        var handler = CreatePropertyHandler(repository, agencies, new FakeListingQuotaPolicy(50), uow);

        await handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId },
            CancellationToken.None);

        uow.Verify(x => x.AcquireAdvisoryLockAsync(
            ListingQuotaLock.ForAgency(agencyId), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.AcquireAdvisoryLockAsync(
            ListingQuotaLock.ForOwner(ownerId), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateProperty_WhenQuotaCheckFails_RollsBackInsteadOfCommitting()
    {
        var ownerId = Guid.NewGuid();
        var repository = new Mock<IPropertyRepository>();
        repository
            .Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var uow = new Mock<IUnitOfWork>();
        var handler = CreatePropertyHandler(
            repository, unitOfWork: uow, quotaPolicy: new FakeListingQuotaPolicy(1));

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId },
            CancellationToken.None));

        // The lock must still have been taken — the race it closes exists precisely because
        // the check itself needs to run inside it — but the transaction must not commit a
        // rejected creation.
        uow.Verify(x => x.AcquireAdvisoryLockAsync(
            ListingQuotaLock.ForOwner(ownerId), It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        repository.Verify(x => x.AddAsync(It.IsAny<Property>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ListingQuotaLock_OwnerAndAgencyKeys_ForTheSameGuid_Differ()
    {
        // Same underlying id used as both an owner id and an agency id must not collide —
        // otherwise an individual owner's lock could serialize against an unrelated agency's.
        var id = Guid.NewGuid();

        Assert.NotEqual(ListingQuotaLock.ForOwner(id), ListingQuotaLock.ForAgency(id));
    }

    [Fact]
    public void ListingQuotaLock_IsDeterministic_ForTheSameId()
    {
        var ownerId = Guid.NewGuid();

        Assert.Equal(ListingQuotaLock.ForOwner(ownerId), ListingQuotaLock.ForOwner(ownerId));
    }

    // ── Application: PropertyDto exposes AgencyId (RELEASE-BLOCKERS-AR.md B-5) ──

    [Fact]
    public void Dto_ExposesAgencyId_WhenListingBelongsToOne()
    {
        var agencyId = Guid.NewGuid();
        var property = CreateListing();
        property.SetAgency(agencyId);

        var dto = PropertyMapper.ToDto(property);

        Assert.Equal(agencyId, dto.AgencyId);
    }

    [Fact]
    public void Dto_ReportsNullAgencyId_ForAnIndependentOwner()
    {
        var property = CreateListing();

        var dto = PropertyMapper.ToDto(property);

        Assert.Null(dto.AgencyId);
    }

    private static UserAccount BuildAccountInAgency(Guid userId, Guid agencyId)
    {
        var account = UserAccount.Create(userId, "نعيم", "بزازة", DateTime.UtcNow);
        account.JoinAgency(agencyId, DateTime.UtcNow);
        account.SelectPlan(Guid.NewGuid(), DateTime.UtcNow);
        return account;
    }

    // ── Application: contact-confirmation + plan-selection gates ──

    [Fact]
    public async Task CreateProperty_WithNeitherEmailNorPhoneConfirmed_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var repository = new Mock<IPropertyRepository>();

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

        var handler = CreatePropertyHandler(repository, identity: identity);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId },
            CancellationToken.None));

        Assert.Equal("LISTING_CONTACT_NOT_CONFIRMED", ex.Code);

        // Rejected before the quota was even consulted, let alone written to.
        repository.Verify(
            x => x.CountActiveListingsByOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        repository.Verify(
            x => x.AddAsync(It.IsAny<Property>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateProperty_WithPhoneConfirmedButNotEmail_IsAllowed()
    {
        // Either confirmation is enough — email is not privileged over phone.
        var ownerId = Guid.NewGuid();
        var repository = new Mock<IPropertyRepository>();
        repository
            .Setup(x => x.CountActiveListingsByOwnerAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        var identity = new Mock<IUserIdentityReadService>();
        identity
            .Setup(x => x.FindByIdAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityAccountSnapshot(
                IdentityId: ownerId,
                UserAccountId: ownerId,
                Email: "owner@example.com",
                PhoneNumber: "+49123456789",
                EmailConfirmed: false,
                PhoneConfirmed: true,
                HasPassword: true,
                IsDeleted: false));

        var handler = CreatePropertyHandler(repository, identity: identity);

        var id = await handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId },
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task CreateProperty_WithNoPlanSelected_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var repository = new Mock<IPropertyRepository>();

        var agencies = new Mock<IAgencyRepository>();
        agencies
            .Setup(x => x.GetUserAccountAsync(ownerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserAccount.Create(ownerId, "نعيم", "بزازة", DateTime.UtcNow));

        var handler = CreatePropertyHandler(repository, agencies);

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            ValidCreateCommand() with { OwnerId = ownerId },
            CancellationToken.None));

        Assert.Equal("LISTING_PLAN_REQUIRED", ex.Code);

        repository.Verify(
            x => x.CountActiveListingsByOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        repository.Verify(
            x => x.AddAsync(It.IsAny<Property>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
        Assert.True(
            newExpiresAt > DateTime.UtcNow.Add(ListingLifecyclePolicy.PublicationPeriod).AddMinutes(-10));
    }

    [Fact]
    public async Task ConfirmExtension_OnPendingFee_PublishesPropertyPublishedEvent()
    {
        // ExtendPublication republishes an expired listing — SocialDistribution must get the same
        // chance to (re-)distribute it as a fresh PATCH /publish would give a new draft.
        var property = CreateListing();
        property.ExpiresAt = DateTime.UtcNow.AddDays(-2);
        property.MarkExpired();

        var fee = BuildPendingFee(property.Id, property.OwnerId);
        var publisher = new Mock<IPublisher>();
        var handler = ConfirmExtensionHandler(property, fee, publisher);

        await handler.Handle(new ConfirmListingExtensionPaymentCommand(fee.Id, Guid.NewGuid()), CancellationToken.None);

        publisher.Verify(
            x => x.Publish(It.Is<PropertyPublishedEvent>(e => e.PropertyId == property.Id), It.IsAny<CancellationToken>()),
            Times.Once);
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

    private static CreatePropertyCommandHandler CreatePropertyHandler(
        Mock<IPropertyRepository> repository,
        Mock<IAgencyRepository>? agencies = null,
        IListingQuotaPolicy? quotaPolicy = null,
        Mock<IUnitOfWork>? unitOfWork = null,
        Mock<IUserIdentityReadService>? identity = null,
        Mock<IPublisher>? publisher = null)
        => new(
            repository.Object,
            (agencies ?? DefaultAgencies()).Object,
            (identity ?? DefaultIdentity()).Object,
            (unitOfWork ?? new Mock<IUnitOfWork>()).Object,
            Mock.Of<ILocationSuggestionService>(),
            quotaPolicy ?? new FakeListingQuotaPolicy(50),
            new PropertyOnlyActiveListingCounter(repository.Object),
            (publisher ?? new Mock<IPublisher>()).Object,
            NullLogger<CreatePropertyCommandHandler>.Instance);

    /// <summary>
    /// Test double for the production IActiveListingCounter (Infrastructure's
    /// ActiveListingCounter, which also folds in Short-Stay listings — see that class's doc
    /// comment). These handler-level tests only ever set up IPropertyRepository's own count
    /// methods and have no interest in Short-Stay, so this simply forwards to the same
    /// mocked repository every existing test already configures, instead of every call site
    /// in this file needing its own IActiveListingCounter mock.
    /// </summary>
    private sealed class PropertyOnlyActiveListingCounter : IActiveListingCounter
    {
        private readonly IPropertyRepository _properties;

        public PropertyOnlyActiveListingCounter(IPropertyRepository properties) => _properties = properties;

        public Task<int> CountActiveListingsByOwnerAsync(Guid ownerId, CancellationToken ct = default)
            => _properties.CountActiveListingsByOwnerAsync(ownerId, ct);

        public Task<int> CountActiveListingsByAgencyAsync(Guid agencyId, CancellationToken ct = default)
            => _properties.CountActiveListingsByAgencyAsync(agencyId, ct);
    }

    /// <summary>
    /// Every owner in these tests has confirmed a way to reach them and picked a plan
    /// unless a test deliberately overrides one of those to exercise the new gates — the
    /// quota/lock/agency behavior these tests actually cover is otherwise unrelated to
    /// either gate, and having every test wire up confirmation + a plan by hand would bury
    /// what each test is actually about.
    /// </summary>
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

    /// <summary>
    /// Deterministic stand-in for the plan-driven IListingQuotaPolicy. The handler-level
    /// tests in this file only need "the policy returned N" — the interesting part of
    /// resolving N from Plan.ListingLimit (including the agency-owner-vs-member-plan
    /// semantics from IListingQuotaPolicy's doc comment) is Infrastructure's
    /// ListingQuotaPolicy's own job and is covered by
    /// PropertyApi.Integration.Tests/Listings/ListingQuotaPolicyTests.cs instead, against
    /// mocked IPlanRepository/IAgencyRepository. Records the account it was called with so a
    /// test can assert the handler asked about the right one.
    /// </summary>
    private sealed class FakeListingQuotaPolicy : IListingQuotaPolicy
    {
        private readonly int _limit;

        public FakeListingQuotaPolicy(int limit)
        {
            _limit = limit;
        }

        public UserAccount? LastQueriedAccount { get; private set; }

        public Task<int> GetActiveListingLimitAsync(
            UserAccount ownerAccount,
            CancellationToken ct = default)
        {
            LastQueriedAccount = ownerAccount;
            return Task.FromResult(_limit);
        }
    }

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
        Transaction fee,
        Mock<IPublisher>? publisher = null)
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
            (publisher ?? new Mock<IPublisher>()).Object,
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
