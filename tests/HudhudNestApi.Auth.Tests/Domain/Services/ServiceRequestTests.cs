using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Auth.Tests.Domain.Services;

[Trait("Category", "Domain")]
[Trait("Entity", "ServiceRequest")]
public sealed class ServiceRequestTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    private static ServiceRequest CreateUnderReview() =>
        ServiceRequest.Create(
            "SR-2026-000001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ServiceCategory.Verification,
            "من فضلكم تحققوا من سند الملكية.",
            UtcNow);

    [Fact(DisplayName = "Create: sets status directly to UnderReview")]
    public void Create_ValidInputs_SetsUnderReview()
    {
        var request = CreateUnderReview();

        Assert.Equal(ServiceRequestStatus.UnderReview, request.Status);
        Assert.Equal("SR-2026-000001", request.RequestNumber);
    }

    [Theory(DisplayName = "Create: missing required id throws DomainException")]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void Create_MissingRequiredId_ThrowsDomainException(
        bool emptyProperty, bool emptyRequester, bool emptyProvider, bool emptyOffering)
    {
        Assert.Throws<DomainException>(() => ServiceRequest.Create(
            "SR-2026-000002",
            emptyProperty ? Guid.Empty : Guid.NewGuid(),
            emptyRequester ? Guid.Empty : Guid.NewGuid(),
            emptyProvider ? Guid.Empty : Guid.NewGuid(),
            emptyOffering ? Guid.Empty : Guid.NewGuid(),
            ServiceCategory.Verification,
            null,
            UtcNow));
    }

    [Fact(DisplayName = "Accept: from UnderReview moves to Accepted and stores the quote")]
    public void Accept_FromUnderReview_MovesToAccepted()
    {
        var request = CreateUnderReview();

        request.Accept(150m, 1, "سنبدأ الأسبوع القادم.", UtcNow);

        Assert.Equal(ServiceRequestStatus.Accepted, request.Status);
        Assert.Equal(150m, request.QuotedPrice);
        Assert.Equal(1, request.QuotedPriceCurrencyId);
    }

    [Fact(DisplayName = "Accept: called twice throws InvalidStateTransitionException")]
    public void Accept_CalledTwice_ThrowsInvalidStateTransitionException()
    {
        var request = CreateUnderReview();
        request.Accept(null, null, null, UtcNow);

        Assert.Throws<InvalidStateTransitionException>(
            () => request.Accept(null, null, null, UtcNow));
    }

    [Fact(DisplayName = "Reject: from UnderReview moves to Rejected and stores the reason")]
    public void Reject_FromUnderReview_MovesToRejected()
    {
        var request = CreateUnderReview();

        request.Reject("خارج نطاق تغطيتنا.", UtcNow);

        Assert.Equal(ServiceRequestStatus.Rejected, request.Status);
        Assert.Equal("خارج نطاق تغطيتنا.", request.RejectionReason);
    }

    [Fact(DisplayName = "Reject: blank reason throws DomainException")]
    public void Reject_BlankReason_ThrowsDomainException()
    {
        var request = CreateUnderReview();

        Assert.Throws<DomainException>(() => request.Reject("   ", UtcNow));
    }

    [Fact(DisplayName = "Schedule: from Accepted moves to Scheduled")]
    public void Schedule_FromAccepted_MovesToScheduled()
    {
        var request = CreateUnderReview();
        request.Accept(null, null, null, UtcNow);

        request.Schedule(UtcNow.AddDays(3), UtcNow);

        Assert.Equal(ServiceRequestStatus.Scheduled, request.Status);
        Assert.Equal(UtcNow.AddDays(3), request.ScheduledAt);
    }

    [Fact(DisplayName = "Schedule: before UnderReview→Accepted throws InvalidStateTransitionException")]
    public void Schedule_BeforeAccepted_ThrowsInvalidStateTransitionException()
    {
        var request = CreateUnderReview();

        Assert.Throws<InvalidStateTransitionException>(
            () => request.Schedule(UtcNow.AddDays(1), UtcNow));
    }

    [Fact(DisplayName = "Schedule: a past date throws DomainException")]
    public void Schedule_PastDate_ThrowsDomainException()
    {
        var request = CreateUnderReview();
        request.Accept(null, null, null, UtcNow);

        Assert.Throws<DomainException>(() => request.Schedule(UtcNow.AddDays(-1), UtcNow));
    }

    [Fact(DisplayName = "Start: from Scheduled moves to InProgress")]
    public void Start_FromScheduled_MovesToInProgress()
    {
        var request = CreateUnderReview();
        request.Accept(null, null, null, UtcNow);
        request.Schedule(UtcNow.AddDays(1), UtcNow);

        request.Start(UtcNow);

        Assert.Equal(ServiceRequestStatus.InProgress, request.Status);
    }

    [Fact(DisplayName = "Complete: from InProgress moves to Completed and stamps CompletedAt")]
    public void Complete_FromInProgress_MovesToCompleted()
    {
        var request = CreateUnderReview();
        request.Accept(null, null, null, UtcNow);
        request.Schedule(UtcNow.AddDays(1), UtcNow);
        request.Start(UtcNow);

        request.Complete(200m, 1, UtcNow);

        Assert.Equal(ServiceRequestStatus.Completed, request.Status);
        Assert.Equal(200m, request.FinalPrice);
        Assert.Equal(UtcNow, request.CompletedAt);
    }

    [Fact(DisplayName = "MarkReviewed: from Completed moves to Reviewed")]
    public void MarkReviewed_FromCompleted_MovesToReviewed()
    {
        var request = CreateUnderReview();
        request.Accept(null, null, null, UtcNow);
        request.Schedule(UtcNow.AddDays(1), UtcNow);
        request.Start(UtcNow);
        request.Complete(null, null, UtcNow);

        request.MarkReviewed(UtcNow);

        Assert.Equal(ServiceRequestStatus.Reviewed, request.Status);
    }

    [Fact(DisplayName = "MarkReviewed: before Completed throws InvalidStateTransitionException")]
    public void MarkReviewed_BeforeCompleted_ThrowsInvalidStateTransitionException()
    {
        var request = CreateUnderReview();

        Assert.Throws<InvalidStateTransitionException>(() => request.MarkReviewed(UtcNow));
    }

    [Theory(DisplayName = "Cancel: allowed from every active state")]
    [InlineData(0)] // UnderReview
    [InlineData(1)] // Accepted
    [InlineData(2)] // Scheduled
    [InlineData(3)] // InProgress
    public void Cancel_FromActiveState_MovesToCancelled(int stepsToAdvance)
    {
        var request = CreateUnderReview();
        if (stepsToAdvance >= 1) request.Accept(null, null, null, UtcNow);
        if (stepsToAdvance >= 2) request.Schedule(UtcNow.AddDays(1), UtcNow);
        if (stepsToAdvance >= 3) request.Start(UtcNow);

        request.Cancel("تغيّرت الخطط.", UtcNow);

        Assert.Equal(ServiceRequestStatus.Cancelled, request.Status);
        Assert.Equal("تغيّرت الخطط.", request.CancellationReason);
    }

    [Fact(DisplayName = "Cancel: after Completed throws InvalidStateTransitionException")]
    public void Cancel_AfterCompleted_ThrowsInvalidStateTransitionException()
    {
        var request = CreateUnderReview();
        request.Accept(null, null, null, UtcNow);
        request.Schedule(UtcNow.AddDays(1), UtcNow);
        request.Start(UtcNow);
        request.Complete(null, null, UtcNow);

        Assert.Throws<InvalidStateTransitionException>(() => request.Cancel(null, UtcNow));
    }

    [Fact(DisplayName = "Cancel: after Rejected throws InvalidStateTransitionException")]
    public void Cancel_AfterRejected_ThrowsInvalidStateTransitionException()
    {
        var request = CreateUnderReview();
        request.Reject("لا يمكن التغطية.", UtcNow);

        Assert.Throws<InvalidStateTransitionException>(() => request.Cancel(null, UtcNow));
    }
}
