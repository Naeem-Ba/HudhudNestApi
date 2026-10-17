using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Bookings.Interfaces;
using HudhudNestApi.Application.Bookings.Services;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Application.Notifications.Localization;
using HudhudNestApi.Domain.Bookings.Entities;
using HudhudNestApi.Domain.Bookings.Enums;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.Tests.Bookings;

/// <summary>QA Phase 8, P8-D08: deleting a listing closes its open visit requests and tells the visitors.</summary>
public sealed class ListingVisitCleanupTests
{
    private static VisitRequest NewVisit(Guid propertyId, Guid requesterId)
        => VisitRequest.Create(propertyId, requesterId, DateTime.UtcNow.AddDays(3), "Sam", "+49 170 1234567");

    [Fact]
    public void OpenVisits_CanBeCancelledBecauseTheListingWasRemoved_AndFinishedOnesAreLeftAlone()
    {
        var pending = NewVisit(Guid.NewGuid(), Guid.NewGuid());
        var confirmed = NewVisit(Guid.NewGuid(), Guid.NewGuid());
        confirmed.Confirm();
        var proposed = NewVisit(Guid.NewGuid(), Guid.NewGuid());
        proposed.ProposeAlternate(DateTime.UtcNow.AddDays(5));
        var declined = NewVisit(Guid.NewGuid(), Guid.NewGuid());
        declined.Decline();
        var completed = NewVisit(Guid.NewGuid(), Guid.NewGuid());
        completed.Confirm();
        completed.Complete();

        Assert.True(pending.CancelBecauseListingRemoved());
        Assert.True(confirmed.CancelBecauseListingRemoved());
        Assert.True(proposed.CancelBecauseListingRemoved());
        Assert.False(declined.CancelBecauseListingRemoved());
        Assert.False(completed.CancelBecauseListingRemoved());

        Assert.Equal(VisitStatus.Cancelled, pending.Status);
        Assert.Equal(VisitStatus.Cancelled, confirmed.Status);
        Assert.Equal(VisitStatus.Cancelled, proposed.Status);
        Assert.Equal(VisitStatus.Declined, declined.Status);
        Assert.Equal(VisitStatus.Completed, completed.Status);
    }

    [Fact]
    public async Task CancelOpenVisitsAsync_ClosesEachVisitSavesOnceAndNotifiesEachVisitor()
    {
        var propertyId = Guid.NewGuid();
        var visitorA = Guid.NewGuid();
        var visitorB = Guid.NewGuid();
        var visits = new List<VisitRequest> { NewVisit(propertyId, visitorA), NewVisit(propertyId, visitorB) };

        var repository = new Mock<IVisitRepository>();
        repository.Setup(x => x.GetOpenByPropertyIdAsync(propertyId, It.IsAny<CancellationToken>())).ReturnsAsync(visits);
        var uow = new Mock<IUnitOfWork>();
        var notifications = new Mock<INotificationService>();
        var sut = new ListingVisitCleanup(repository.Object, uow.Object, notifications.Object, NullLogger<ListingVisitCleanup>.Instance);

        var count = await sut.CancelOpenVisitsAsync(propertyId, "شقة", CancellationToken.None);

        Assert.Equal(2, count);
        Assert.All(visits, visit => Assert.Equal(VisitStatus.Cancelled, visit.Status));
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        foreach (var visitor in new[] { visitorA, visitorB })
        {
            notifications.Verify(x => x.NotifyPropertyUpdateAsync(
                visitor, propertyId, "شقة", NotificationType.VisitCancelledByHost,
                It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task CancelOpenVisitsAsync_WithNoOpenVisits_DoesNotSaveOrNotify()
    {
        var repository = new Mock<IVisitRepository>();
        repository.Setup(x => x.GetOpenByPropertyIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<VisitRequest>());
        var uow = new Mock<IUnitOfWork>();
        var notifications = new Mock<INotificationService>();
        var sut = new ListingVisitCleanup(repository.Object, uow.Object, notifications.Object, NullLogger<ListingVisitCleanup>.Instance);

        var count = await sut.CancelOpenVisitsAsync(Guid.NewGuid(), "x", CancellationToken.None);

        Assert.Equal(0, count);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        notifications.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CancelOpenVisitsAsync_WhenOneNotificationFails_StillClosesAndNotifiesTheRest()
    {
        var propertyId = Guid.NewGuid();
        var failing = Guid.NewGuid();
        var other = Guid.NewGuid();
        var visits = new List<VisitRequest> { NewVisit(propertyId, failing), NewVisit(propertyId, other) };
        var repository = new Mock<IVisitRepository>();
        repository.Setup(x => x.GetOpenByPropertyIdAsync(propertyId, It.IsAny<CancellationToken>())).ReturnsAsync(visits);
        var notifications = new Mock<INotificationService>();
        notifications
            .Setup(x => x.NotifyPropertyUpdateAsync(
                failing, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<NotificationType>(),
                It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("push down"));
        var sut = new ListingVisitCleanup(repository.Object, Mock.Of<IUnitOfWork>(), notifications.Object, NullLogger<ListingVisitCleanup>.Instance);

        var count = await sut.CancelOpenVisitsAsync(propertyId, "x", CancellationToken.None);

        Assert.Equal(2, count);
        notifications.Verify(x => x.NotifyPropertyUpdateAsync(
            other, propertyId, "x", NotificationType.VisitCancelledByHost,
            It.IsAny<NotificationText>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
