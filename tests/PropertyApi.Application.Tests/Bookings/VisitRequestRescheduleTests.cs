using PropertyApi.Domain.Bookings.Entities;
using PropertyApi.Domain.Bookings.Enums;
using PropertyApi.Domain.Common.Exceptions;

namespace PropertyApi.Application.Tests.Bookings;

/// <summary>
/// Domain-level unit tests for VisitRequest's reschedule state machine
/// (ProposeAlternate/AcceptReschedule/DeclineReschedule), added by
/// fix/visit-request-notification-actions. No mocks needed — pure entity behavior,
/// mirrors UserAccountSubscriptionTests' sibling coverage for the admin PR.
///
/// Before this PR there was zero test coverage anywhere for VisitRequest (Confirm/
/// Decline/Cancel/Complete were untested too) — this file focuses on the new
/// reschedule methods the PR actually adds, per the audit's changed-code scope.
/// </summary>
public sealed class VisitRequestRescheduleTests
{
    private static VisitRequest CreatePendingVisit(DateTime proposedAt)
        => VisitRequest.Create(
            propertyId: Guid.NewGuid(),
            requesterId: Guid.NewGuid(),
            proposedAt: proposedAt,
            visitorName: "زائر",
            visitorPhone: "0999999999");

    [Fact]
    public void ProposeAlternate_FromPending_MovesToRescheduleProposed_AndUpdatesProposedAtAndNote()
    {
        var visit = CreatePendingVisit(DateTime.UtcNow.AddDays(1));
        var newTime = DateTime.UtcNow.AddDays(3);

        visit.ProposeAlternate(newTime, "موعد أنسب للمالك");

        Assert.Equal(VisitStatus.RescheduleProposed, visit.Status);
        Assert.Equal(newTime, visit.ProposedAt);
        Assert.Equal("موعد أنسب للمالك", visit.OwnerNote);
        Assert.NotNull(visit.RespondedAt);
    }

    [Fact]
    public void ProposeAlternate_TrimsOwnerNote()
    {
        var visit = CreatePendingVisit(DateTime.UtcNow.AddDays(1));

        visit.ProposeAlternate(DateTime.UtcNow.AddDays(3), "  ملاحظة  ");

        Assert.Equal("ملاحظة", visit.OwnerNote);
    }

    [Theory]
    [InlineData(VisitStatus.Confirmed)]
    [InlineData(VisitStatus.Declined)]
    [InlineData(VisitStatus.Cancelled)]
    [InlineData(VisitStatus.Completed)]
    [InlineData(VisitStatus.RescheduleProposed)]
    public void ProposeAlternate_WhenNotPending_Throws(VisitStatus nonPendingStatus)
    {
        var visit = CreatePendingVisit(DateTime.UtcNow.AddDays(1));
        MoveTo(visit, nonPendingStatus);

        Assert.Throws<DomainException>(() =>
            visit.ProposeAlternate(DateTime.UtcNow.AddDays(3)));
    }

    [Fact]
    public void ProposeAlternate_LessThanTwoHoursFromNow_Throws()
    {
        var visit = CreatePendingVisit(DateTime.UtcNow.AddDays(1));

        Assert.Throws<DomainException>(() =>
            visit.ProposeAlternate(DateTime.UtcNow.AddHours(1)));
    }

    [Fact]
    public void ProposeAlternate_MoreThanNinetyDaysOut_Throws()
    {
        var visit = CreatePendingVisit(DateTime.UtcNow.AddDays(1));

        Assert.Throws<DomainException>(() =>
            visit.ProposeAlternate(DateTime.UtcNow.AddDays(91)));
    }

    [Fact]
    public void AcceptReschedule_FromRescheduleProposed_MovesToConfirmed()
    {
        var visit = CreatePendingVisit(DateTime.UtcNow.AddDays(1));
        visit.ProposeAlternate(DateTime.UtcNow.AddDays(3));
        var respondedAtAfterPropose = visit.RespondedAt;

        visit.AcceptReschedule();

        Assert.Equal(VisitStatus.Confirmed, visit.Status);
        Assert.NotNull(visit.RespondedAt);
        Assert.True(visit.RespondedAt >= respondedAtAfterPropose);
    }

    [Theory]
    [InlineData(VisitStatus.Pending)]
    [InlineData(VisitStatus.Confirmed)]
    [InlineData(VisitStatus.Declined)]
    [InlineData(VisitStatus.Cancelled)]
    [InlineData(VisitStatus.Completed)]
    public void AcceptReschedule_WhenNotRescheduleProposed_Throws(VisitStatus otherStatus)
    {
        var visit = CreatePendingVisit(DateTime.UtcNow.AddDays(1));
        MoveTo(visit, otherStatus);

        Assert.Throws<DomainException>(() => visit.AcceptReschedule());
    }

    [Fact]
    public void DeclineReschedule_FromRescheduleProposed_MovesToDeclined()
    {
        var visit = CreatePendingVisit(DateTime.UtcNow.AddDays(1));
        visit.ProposeAlternate(DateTime.UtcNow.AddDays(3));

        visit.DeclineReschedule();

        Assert.Equal(VisitStatus.Declined, visit.Status);
    }

    [Theory]
    [InlineData(VisitStatus.Pending)]
    [InlineData(VisitStatus.Confirmed)]
    [InlineData(VisitStatus.Declined)]
    [InlineData(VisitStatus.Cancelled)]
    [InlineData(VisitStatus.Completed)]
    public void DeclineReschedule_WhenNotRescheduleProposed_Throws(VisitStatus otherStatus)
    {
        var visit = CreatePendingVisit(DateTime.UtcNow.AddDays(1));
        MoveTo(visit, otherStatus);

        Assert.Throws<DomainException>(() => visit.DeclineReschedule());
    }

    [Fact]
    public void Cancel_FromRescheduleProposed_Succeeds()
    {
        // Regression guard for the actual product requirement behind this PR: a requester
        // awaiting a reschedule decision must still be able to bail out entirely, not just
        // accept/decline the alternate.
        var visit = CreatePendingVisit(DateTime.UtcNow.AddDays(1));
        visit.ProposeAlternate(DateTime.UtcNow.AddDays(3));

        visit.Cancel(visit.RequesterId);

        Assert.Equal(VisitStatus.Cancelled, visit.Status);
    }

    /// <summary>Drives a fresh Pending visit into <paramref name="status"/> via the real
    /// domain methods only — never reflection — so each seeded fixture is itself a
    /// state the state machine actually allows.</summary>
    private static void MoveTo(VisitRequest visit, VisitStatus status)
    {
        switch (status)
        {
            case VisitStatus.Pending:
                break;
            case VisitStatus.Confirmed:
                visit.Confirm();
                break;
            case VisitStatus.Declined:
                visit.Decline();
                break;
            case VisitStatus.Cancelled:
                visit.Cancel(visit.RequesterId);
                break;
            case VisitStatus.Completed:
                visit.Confirm();
                visit.Complete();
                break;
            case VisitStatus.RescheduleProposed:
                visit.ProposeAlternate(DateTime.UtcNow.AddDays(3));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }
    }
}
