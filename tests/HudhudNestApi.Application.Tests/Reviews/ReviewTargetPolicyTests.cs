using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Reviews.Commands.RateUser;
using HudhudNestApi.Application.Reviews.Interfaces;
using HudhudNestApi.Application.Reviews.Services;
using HudhudNestApi.Application.Bookings.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Application.Users.Messaging.Interfaces;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Reviews.Entities;
using HudhudNestApi.Domain.Users.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace HudhudNestApi.Application.Tests.Reviews;

/// <summary>Owner decision 2026-10-18: only accounts with a plan in force can receive a new review.</summary>
public sealed class ReviewTargetPolicyTests
{
    private static UserAccount Account(bool withPlan, DateTime? expiresAt = null)
    {
        var account = UserAccount.Create(Guid.NewGuid(), "Test", "Host", DateTime.UtcNow);
        if (withPlan)
            account.ActivatePlanByAdmin(Guid.NewGuid(), expiresAt ?? DateTime.UtcNow.AddDays(30), account.Id, DateTime.UtcNow);
        return account;
    }

    private static ReviewTargetPolicy PolicyFor(UserAccount? account)
    {
        var accounts = new Mock<IUserAccountRepository>();
        accounts.Setup(a => a.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(account);
        return new ReviewTargetPolicy(accounts.Object);
    }

    [Fact]
    public async Task AccountWithActivePlan_CanBeReviewed()
    {
        var policy = PolicyFor(Account(withPlan: true));
        await policy.EnsureReviewableAsync(Guid.NewGuid());
        Assert.True(await policy.IsReviewableAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AccountWithoutPlan_CannotBeReviewed()
    {
        var policy = PolicyFor(Account(withPlan: false));
        var ex = await Assert.ThrowsAsync<DomainException>(() => policy.EnsureReviewableAsync(Guid.NewGuid()));
        Assert.Equal(ReviewTargetPolicy.ReviewTargetInactiveMessage, ex.Message);
        Assert.False(await policy.IsReviewableAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AccountWithExpiredPlan_CannotBeReviewed()
    {
        var policy = PolicyFor(Account(withPlan: true, expiresAt: DateTime.UtcNow.AddSeconds(1)));
        // The plan is in force now but not after it ends; an already-expired one is refused.
        var expired = Account(withPlan: true, expiresAt: DateTime.UtcNow.AddDays(30));
        expired.CancelPlan(expired.Id, DateTime.UtcNow);
        await Assert.ThrowsAsync<DomainException>(() => PolicyFor(expired).EnsureReviewableAsync(Guid.NewGuid()));
        await policy.EnsureReviewableAsync(Guid.NewGuid());
    }

    [Fact]
    public async Task MissingAccount_IsNotFound()
    {
        var policy = PolicyFor(null);
        await Assert.ThrowsAsync<NotFoundException>(() => policy.EnsureReviewableAsync(Guid.NewGuid()));
        Assert.False(await policy.IsReviewableAsync(Guid.NewGuid()));
    }

    private static RateUserCommand Rate(Guid rated, Guid rater)
        => new(rated, rater, Credibility: 5, Safety: 5, ResponseSpeed: 5, Transparency: 5, Comment: null);

    private static (RateUserCommandHandler Handler, Mock<IUserRatingRepository> Ratings) RateHandler(
        UserAccount rated, UserRating? existing)
    {
        var ratings = new Mock<IUserRatingRepository>();
        ratings.Setup(r => r.GetByRaterAndRatedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var accounts = new Mock<IUserAccountRepository>();
        accounts.Setup(a => a.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(rated);

        var visits = new Mock<IVisitRepository>();
        visits.Setup(v => v.HasCompletedVisitWithOwnerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var handler = new RateUserCommandHandler(
            ratings.Object, accounts.Object, visits.Object, Mock.Of<IMessageRepository>(),
            new ReviewTargetPolicy(accounts.Object), Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(),
            NullLogger<RateUserCommandHandler>.Instance);
        return (handler, ratings);
    }

    [Fact]
    public async Task RateUser_NewRating_OfAccountWithoutPlan_IsRefused()
    {
        var (handler, ratings) = RateHandler(Account(withPlan: false), existing: null);

        await Assert.ThrowsAsync<DomainException>(() => handler.Handle(Rate(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));
        ratings.Verify(r => r.AddAsync(It.IsAny<UserRating>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RateUser_EditingAnExistingRating_StillWorksAfterThePlanEnded()
    {
        var ratedId = Guid.NewGuid();
        var raterId = Guid.NewGuid();
        var existing = UserRating.Create(ratedId, raterId, 3, 3, 3, 3, null);
        var (handler, ratings) = RateHandler(Account(withPlan: false), existing);

        await handler.Handle(Rate(ratedId, raterId), CancellationToken.None);

        ratings.Verify(r => r.Update(existing), Times.Once);
    }
}
