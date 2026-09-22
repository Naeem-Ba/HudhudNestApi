using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using HudhudNestApi.Application.Bookings.Commands.AcceptRescheduledVisit;
using HudhudNestApi.Application.Bookings.Commands.CancelVisit;
using HudhudNestApi.Application.Bookings.Commands.ConfirmVisit;
using HudhudNestApi.Application.Bookings.Commands.DeclineRescheduledVisit;
using HudhudNestApi.Application.Bookings.Commands.DeclineVisit;
using HudhudNestApi.Application.Bookings.Commands.ProposeAlternateVisit;
using HudhudNestApi.Application.Bookings.Commands.RequestVisit;
using HudhudNestApi.Application.Bookings.Interfaces;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Notifications.Interfaces;
using HudhudNestApi.Domain.Bookings.Entities;
using HudhudNestApi.Domain.Bookings.Enums;
using HudhudNestApi.Domain.Notifications.Enums;

namespace HudhudNestApi.Application.Tests.Bookings;

/// <summary>
/// Handler-level unit tests for fix/visit-request-notification-actions:
/// - the three new reschedule commands (ProposeAlternate/AcceptReschedule/DeclineReschedule),
/// - the regression the PR's "notification sends weren't fault-isolated" fix claims for
///   Confirm/Decline/Cancel (a failing notification must never turn an already-persisted
///   state change into a 500),
/// - the regression for RequestVisit's "visitor's note was dropped" fix.
///
/// Mirrors AgencyInvitationTests' style: Moq for collaborators, Mock.Of&lt;IUnitOfWork&gt;()
/// for the no-op unit of work, real VisitRequest/PropertySummary instances (no DB).
/// </summary>
public sealed class VisitCommandHandlerTests
{
    private static VisitRequest CreatePendingVisit(Guid requesterId, out Guid propertyId)
    {
        propertyId = Guid.NewGuid();
        return VisitRequest.Create(
            propertyId, requesterId, DateTime.UtcNow.AddDays(1), "زائر", "0999999999", "ملاحظة الزائر");
    }

    private static Mock<IVisitRepository> VisitsReturning(VisitRequest? visit)
    {
        var repo = new Mock<IVisitRepository>();
        repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(visit);
        return repo;
    }

    private static Mock<IPropertyReadRepository> PropertiesReturning(PropertySummary? property)
    {
        var repo = new Mock<IPropertyReadRepository>();
        repo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(property);
        return repo;
    }

    private static PropertySummary MakeProperty(Guid propertyId, Guid ownerId)
        => new(propertyId, "شقة للإيجار", "دمشق", ownerId, IsPublished: true, MainImageUrl: null);

    // ── ProposeAlternateVisitCommandHandler ────────────────────────

    [Fact]
    public async Task ProposeAlternate_ByOwner_TransitionsVisit_SavesChanges_AndNotifiesRequester()
    {
        var requesterId = Guid.NewGuid();
        var visit = CreatePendingVisit(requesterId, out var propertyId);
        var ownerId = Guid.NewGuid();
        var property = MakeProperty(propertyId, ownerId);
        var newTime = DateTime.UtcNow.AddDays(4);

        var visits = VisitsReturning(visit);
        var properties = PropertiesReturning(property);
        var uow = new Mock<IUnitOfWork>();
        var notifications = new Mock<INotificationService>();

        var handler = new ProposeAlternateVisitCommandHandler(
            visits.Object, uow.Object, notifications.Object, properties.Object,
            NullLogger<ProposeAlternateVisitCommandHandler>.Instance);

        var result = await handler.Handle(
            new ProposeAlternateVisitCommand(visit.Id, ownerId, newTime, "موعد أنسب"),
            CancellationToken.None);

        Assert.True(result);
        Assert.Equal(VisitStatus.RescheduleProposed, visit.Status);
        Assert.Equal(newTime, visit.ProposedAt);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(
            x => x.NotifyPropertyUpdateAsync(
                requesterId, propertyId, property.Title,
                NotificationType.VisitRescheduleProposed, It.IsAny<string>(),
                visit.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProposeAlternate_ByNonOwner_IsForbidden_AndDoesNotSave()
    {
        var visit = CreatePendingVisit(Guid.NewGuid(), out var propertyId);
        var property = MakeProperty(propertyId, ownerId: Guid.NewGuid());

        var visits = VisitsReturning(visit);
        var properties = PropertiesReturning(property);
        var uow = new Mock<IUnitOfWork>();

        var handler = new ProposeAlternateVisitCommandHandler(
            visits.Object, uow.Object, Mock.Of<INotificationService>(), properties.Object,
            NullLogger<ProposeAlternateVisitCommandHandler>.Instance);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new ProposeAlternateVisitCommand(visit.Id, Guid.NewGuid(), DateTime.UtcNow.AddDays(4)),
            CancellationToken.None));

        Assert.Equal(VisitStatus.Pending, visit.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProposeAlternate_VisitNotFound_Throws()
    {
        var handler = new ProposeAlternateVisitCommandHandler(
            VisitsReturning(null).Object, Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(),
            Mock.Of<IPropertyReadRepository>(), NullLogger<ProposeAlternateVisitCommandHandler>.Instance);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            new ProposeAlternateVisitCommand(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(4)),
            CancellationToken.None));
    }

    [Fact]
    public async Task ProposeAlternate_WhenNotificationThrows_StillSucceeds()
    {
        // Fault isolation: the visit is already re-proposed and saved before the
        // notification is attempted — a SignalR/notification failure must not surface
        // as a failure of the propose-alternate request itself.
        var visit = CreatePendingVisit(Guid.NewGuid(), out var propertyId);
        var ownerId = Guid.NewGuid();
        var property = MakeProperty(propertyId, ownerId);

        var notifications = new Mock<INotificationService>();
        notifications
            .Setup(x => x.NotifyPropertyUpdateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<NotificationType>(),
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated notification failure"));

        var handler = new ProposeAlternateVisitCommandHandler(
            VisitsReturning(visit).Object, Mock.Of<IUnitOfWork>(), notifications.Object,
            PropertiesReturning(property).Object, NullLogger<ProposeAlternateVisitCommandHandler>.Instance);

        var result = await handler.Handle(
            new ProposeAlternateVisitCommand(visit.Id, ownerId, DateTime.UtcNow.AddDays(4)),
            CancellationToken.None);

        Assert.True(result);
        Assert.Equal(VisitStatus.RescheduleProposed, visit.Status);
    }

    // ── AcceptRescheduledVisitCommandHandler ───────────────────────

    [Fact]
    public async Task AcceptReschedule_ByRequester_ConfirmsVisit_AndNotifiesOwner()
    {
        var requesterId = Guid.NewGuid();
        var visit = CreatePendingVisit(requesterId, out var propertyId);
        visit.ProposeAlternate(DateTime.UtcNow.AddDays(4));
        var ownerId = Guid.NewGuid();
        var property = MakeProperty(propertyId, ownerId);

        var uow = new Mock<IUnitOfWork>();
        var notifications = new Mock<INotificationService>();

        var handler = new AcceptRescheduledVisitCommandHandler(
            VisitsReturning(visit).Object, uow.Object, notifications.Object,
            PropertiesReturning(property).Object, NullLogger<AcceptRescheduledVisitCommandHandler>.Instance);

        var result = await handler.Handle(
            new AcceptRescheduledVisitCommand(visit.Id, requesterId), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(VisitStatus.Confirmed, visit.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(
            x => x.NotifyPropertyUpdateAsync(
                ownerId, propertyId, property.Title, NotificationType.VisitRescheduleAccepted,
                It.IsAny<string>(), visit.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AcceptReschedule_ByNonRequester_IsForbidden_AndDoesNotSave()
    {
        var visit = CreatePendingVisit(Guid.NewGuid(), out var propertyId);
        visit.ProposeAlternate(DateTime.UtcNow.AddDays(4));
        var property = MakeProperty(propertyId, Guid.NewGuid());
        var uow = new Mock<IUnitOfWork>();

        var handler = new AcceptRescheduledVisitCommandHandler(
            VisitsReturning(visit).Object, uow.Object, Mock.Of<INotificationService>(),
            PropertiesReturning(property).Object, NullLogger<AcceptRescheduledVisitCommandHandler>.Instance);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new AcceptRescheduledVisitCommand(visit.Id, Guid.NewGuid()), CancellationToken.None));

        Assert.Equal(VisitStatus.RescheduleProposed, visit.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AcceptReschedule_WhenNotRescheduleProposed_PropagatesDomainException()
    {
        // e.g. the requester double-clicks accept after it already went through — the
        // domain's own guard must still be enforced at the handler boundary.
        var requesterId = Guid.NewGuid();
        var visit = CreatePendingVisit(requesterId, out var propertyId); // still Pending
        var property = MakeProperty(propertyId, Guid.NewGuid());

        var handler = new AcceptRescheduledVisitCommandHandler(
            VisitsReturning(visit).Object, Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(),
            PropertiesReturning(property).Object, NullLogger<AcceptRescheduledVisitCommandHandler>.Instance);

        await Assert.ThrowsAsync<HudhudNestApi.Domain.Common.Exceptions.DomainException>(() => handler.Handle(
            new AcceptRescheduledVisitCommand(visit.Id, requesterId), CancellationToken.None));
    }

    [Fact]
    public async Task AcceptReschedule_WhenNotificationThrows_StillSucceeds()
    {
        var requesterId = Guid.NewGuid();
        var visit = CreatePendingVisit(requesterId, out var propertyId);
        visit.ProposeAlternate(DateTime.UtcNow.AddDays(4));
        var property = MakeProperty(propertyId, Guid.NewGuid());

        var notifications = new Mock<INotificationService>();
        notifications
            .Setup(x => x.NotifyPropertyUpdateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<NotificationType>(),
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated notification failure"));

        var handler = new AcceptRescheduledVisitCommandHandler(
            VisitsReturning(visit).Object, Mock.Of<IUnitOfWork>(), notifications.Object,
            PropertiesReturning(property).Object, NullLogger<AcceptRescheduledVisitCommandHandler>.Instance);

        var result = await handler.Handle(
            new AcceptRescheduledVisitCommand(visit.Id, requesterId), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(VisitStatus.Confirmed, visit.Status);
    }

    // ── DeclineRescheduledVisitCommandHandler ──────────────────────

    [Fact]
    public async Task DeclineReschedule_ByRequester_DeclinesVisit_AndNotifiesOwner()
    {
        var requesterId = Guid.NewGuid();
        var visit = CreatePendingVisit(requesterId, out var propertyId);
        visit.ProposeAlternate(DateTime.UtcNow.AddDays(4));
        var ownerId = Guid.NewGuid();
        var property = MakeProperty(propertyId, ownerId);

        var uow = new Mock<IUnitOfWork>();
        var notifications = new Mock<INotificationService>();

        var handler = new DeclineRescheduledVisitCommandHandler(
            VisitsReturning(visit).Object, uow.Object, notifications.Object,
            PropertiesReturning(property).Object, NullLogger<DeclineRescheduledVisitCommandHandler>.Instance);

        var result = await handler.Handle(
            new DeclineRescheduledVisitCommand(visit.Id, requesterId), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(VisitStatus.Declined, visit.Status);
        notifications.Verify(
            x => x.NotifyPropertyUpdateAsync(
                ownerId, propertyId, property.Title, NotificationType.VisitRescheduleDeclined,
                It.IsAny<string>(), visit.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DeclineReschedule_ByNonRequester_IsForbidden()
    {
        var visit = CreatePendingVisit(Guid.NewGuid(), out var propertyId);
        visit.ProposeAlternate(DateTime.UtcNow.AddDays(4));
        var property = MakeProperty(propertyId, Guid.NewGuid());

        var handler = new DeclineRescheduledVisitCommandHandler(
            VisitsReturning(visit).Object, Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(),
            PropertiesReturning(property).Object, NullLogger<DeclineRescheduledVisitCommandHandler>.Instance);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            new DeclineRescheduledVisitCommand(visit.Id, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task DeclineReschedule_WhenNotificationThrows_StillSucceeds()
    {
        var requesterId = Guid.NewGuid();
        var visit = CreatePendingVisit(requesterId, out var propertyId);
        visit.ProposeAlternate(DateTime.UtcNow.AddDays(4));
        var property = MakeProperty(propertyId, Guid.NewGuid());

        var notifications = new Mock<INotificationService>();
        notifications
            .Setup(x => x.NotifyPropertyUpdateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<NotificationType>(),
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated notification failure"));

        var handler = new DeclineRescheduledVisitCommandHandler(
            VisitsReturning(visit).Object, Mock.Of<IUnitOfWork>(), notifications.Object,
            PropertiesReturning(property).Object, NullLogger<DeclineRescheduledVisitCommandHandler>.Instance);

        var result = await handler.Handle(
            new DeclineRescheduledVisitCommand(visit.Id, requesterId), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(VisitStatus.Declined, visit.Status);
    }

    // ── Confirm/Decline/Cancel: ordinary success path ──────────────
    // Neither handler had any test coverage before this PR (the audit found zero tests for
    // VisitRequest/VisitsController anywhere). These cover the plain non-throwing path —
    // the fault-isolation tests below only ever exercise the catch branch.

    [Fact]
    public async Task Confirm_ByOwner_ConfirmsVisit_SavesChanges_AndNotifiesRequester()
    {
        var requesterId = Guid.NewGuid();
        var visit = CreatePendingVisit(requesterId, out var propertyId);
        var ownerId = Guid.NewGuid();
        var property = MakeProperty(propertyId, ownerId);

        var uow = new Mock<IUnitOfWork>();
        var notifications = new Mock<INotificationService>();

        var handler = new ConfirmVisitCommandHandler(
            VisitsReturning(visit).Object, uow.Object, notifications.Object,
            PropertiesReturning(property).Object, NullLogger<ConfirmVisitCommandHandler>.Instance);

        var result = await handler.Handle(
            new ConfirmVisitCommand(visit.Id, ownerId, "أهلاً بكم"), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(VisitStatus.Confirmed, visit.Status);
        Assert.Equal("أهلاً بكم", visit.OwnerNote);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(
            x => x.NotifyPropertyUpdateAsync(
                requesterId, propertyId, property.Title, NotificationType.VisitConfirmed,
                It.IsAny<string>(), visit.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Decline_ByOwner_DeclinesVisit_SavesChanges_AndNotifiesRequester()
    {
        var requesterId = Guid.NewGuid();
        var visit = CreatePendingVisit(requesterId, out var propertyId);
        var ownerId = Guid.NewGuid();
        var property = MakeProperty(propertyId, ownerId);

        var uow = new Mock<IUnitOfWork>();
        var notifications = new Mock<INotificationService>();

        var handler = new DeclineVisitCommandHandler(
            VisitsReturning(visit).Object, uow.Object, notifications.Object,
            PropertiesReturning(property).Object, NullLogger<DeclineVisitCommandHandler>.Instance);

        var result = await handler.Handle(
            new DeclineVisitCommand(visit.Id, ownerId, "غير متاح"), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(VisitStatus.Declined, visit.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(
            x => x.NotifyPropertyUpdateAsync(
                requesterId, propertyId, property.Title, NotificationType.VisitDeclined,
                It.IsAny<string>(), visit.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Cancel_ByRequester_CancelsVisit_SavesChanges_AndNotifiesOwner()
    {
        var requesterId = Guid.NewGuid();
        var visit = CreatePendingVisit(requesterId, out var propertyId);
        var ownerId = Guid.NewGuid();
        var property = MakeProperty(propertyId, ownerId);

        var uow = new Mock<IUnitOfWork>();
        var notifications = new Mock<INotificationService>();

        var handler = new CancelVisitCommandHandler(
            VisitsReturning(visit).Object, uow.Object, notifications.Object,
            PropertiesReturning(property).Object, NullLogger<CancelVisitCommandHandler>.Instance);

        var result = await handler.Handle(
            new CancelVisitCommand(visit.Id, requesterId), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(VisitStatus.Cancelled, visit.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        notifications.Verify(
            x => x.NotifyPropertyUpdateAsync(
                ownerId, propertyId, property.Title, NotificationType.VisitCancelled,
                It.IsAny<string>(), visit.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Fault-isolation regression: Confirm/Decline/Cancel ─────────
    // Before this PR, ConfirmVisit/DeclineVisit/CancelVisit called NotifyPropertyUpdateAsync
    // with no try/catch — an exception there (e.g. SignalR unreachable) propagated out of
    // Handle() and turned an already-persisted state change into an HTTP 500, even though
    // the visit's new status was already saved. These tests prove notification failures no
    // longer do that, for all three handlers.

    [Fact]
    public async Task Confirm_WhenNotificationThrows_StillSucceeds_AndVisitStaysConfirmed()
    {
        var requesterId = Guid.NewGuid();
        var visit = CreatePendingVisit(requesterId, out var propertyId);
        var ownerId = Guid.NewGuid();
        var property = MakeProperty(propertyId, ownerId);

        var notifications = new Mock<INotificationService>();
        notifications
            .Setup(x => x.NotifyPropertyUpdateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<NotificationType>(),
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated notification failure"));

        var uow = new Mock<IUnitOfWork>();
        var handler = new ConfirmVisitCommandHandler(
            VisitsReturning(visit).Object, uow.Object, notifications.Object,
            PropertiesReturning(property).Object, NullLogger<ConfirmVisitCommandHandler>.Instance);

        var result = await handler.Handle(
            new ConfirmVisitCommand(visit.Id, ownerId), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(VisitStatus.Confirmed, visit.Status);
        uow.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Decline_WhenNotificationThrows_StillSucceeds_AndVisitStaysDeclined()
    {
        var requesterId = Guid.NewGuid();
        var visit = CreatePendingVisit(requesterId, out var propertyId);
        var property = MakeProperty(propertyId, Guid.NewGuid());

        var notifications = new Mock<INotificationService>();
        notifications
            .Setup(x => x.NotifyPropertyUpdateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<NotificationType>(),
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated notification failure"));

        var handler = new DeclineVisitCommandHandler(
            VisitsReturning(visit).Object, Mock.Of<IUnitOfWork>(), notifications.Object,
            PropertiesReturning(property).Object, NullLogger<DeclineVisitCommandHandler>.Instance);

        var result = await handler.Handle(
            new DeclineVisitCommand(visit.Id, property.OwnerId), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(VisitStatus.Declined, visit.Status);
    }

    [Fact]
    public async Task Cancel_WhenNotificationThrows_StillSucceeds_AndVisitStaysCancelled()
    {
        var requesterId = Guid.NewGuid();
        var visit = CreatePendingVisit(requesterId, out var propertyId);
        var property = MakeProperty(propertyId, Guid.NewGuid());

        var notifications = new Mock<INotificationService>();
        notifications
            .Setup(x => x.NotifyPropertyUpdateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<NotificationType>(),
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated notification failure"));

        var handler = new CancelVisitCommandHandler(
            VisitsReturning(visit).Object, Mock.Of<IUnitOfWork>(), notifications.Object,
            PropertiesReturning(property).Object, NullLogger<CancelVisitCommandHandler>.Instance);

        var result = await handler.Handle(
            new CancelVisitCommand(visit.Id, requesterId), CancellationToken.None);

        Assert.True(result);
        Assert.Equal(VisitStatus.Cancelled, visit.Status);
    }

    // ── RequestVisit regression: visitor's note reaches the owner ──

    [Fact]
    public async Task RequestVisit_IncludesVisitorNoteInNotificationDetail_AndPassesRelatedEntityId()
    {
        var ownerId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var property = MakeProperty(propertyId, ownerId);

        var visits = new Mock<IVisitRepository>();
        visits.Setup(x => x.HasPendingVisitAsync(propertyId, requesterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        string? capturedDetail = null;
        Guid? capturedRelatedEntityId = null;
        var notifications = new Mock<INotificationService>();
        notifications
            .Setup(x => x.NotifyPropertyUpdateAsync(
                ownerId, propertyId, property.Title, NotificationType.VisitRequested,
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, string, NotificationType, string, Guid?, CancellationToken>(
                (_, _, _, _, detail, relatedEntityId, _) =>
                {
                    capturedDetail = detail;
                    capturedRelatedEntityId = relatedEntityId;
                })
            .Returns(Task.CompletedTask);

        var handler = new RequestVisitCommandHandler(
            visits.Object, Mock.Of<IUnitOfWork>(), notifications.Object,
            PropertiesReturning(property).Object, NullLogger<RequestVisitCommandHandler>.Instance);

        var result = await handler.Handle(
            new RequestVisitCommand(
                propertyId, requesterId, DateTime.UtcNow.AddDays(1), "زائر", "0999999999",
                "الرجاء الاتصال قبل الوصول"),
            CancellationToken.None);

        Assert.Contains("الرجاء الاتصال قبل الوصول", capturedDetail);
        Assert.Equal(result.Id, capturedRelatedEntityId);
    }

    [Fact]
    public async Task RequestVisit_WhenAPendingRescheduleAlreadyExists_IsRejected()
    {
        // Regression for VisitRepository.HasPendingVisitAsync now also counting
        // RescheduleProposed as pending: the handler must honor whatever the repository
        // reports, including that new case.
        var ownerId = Guid.NewGuid();
        var requesterId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var property = MakeProperty(propertyId, ownerId);

        var visits = new Mock<IVisitRepository>();
        visits.Setup(x => x.HasPendingVisitAsync(propertyId, requesterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // simulates an existing RescheduleProposed row

        var handler = new RequestVisitCommandHandler(
            visits.Object, Mock.Of<IUnitOfWork>(), Mock.Of<INotificationService>(),
            PropertiesReturning(property).Object, NullLogger<RequestVisitCommandHandler>.Instance);

        await Assert.ThrowsAsync<HudhudNestApi.Domain.Common.Exceptions.DomainException>(() => handler.Handle(
            new RequestVisitCommand(
                propertyId, requesterId, DateTime.UtcNow.AddDays(1), "زائر", "0999999999", null),
            CancellationToken.None));
    }
}
