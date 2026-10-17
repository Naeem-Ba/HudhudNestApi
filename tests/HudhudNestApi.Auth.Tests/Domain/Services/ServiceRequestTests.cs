using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Services.Entities;
using HudhudNestApi.Domain.Services.Enums;

namespace HudhudNestApi.Auth.Tests.Domain.Services;

[Trait("Category", "Domain")]
[Trait("Entity", "ServiceRequest")]
public sealed class ServiceRequestTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

    private static ServiceRequest CreateInquiry(ServiceRequestIntent intent = ServiceRequestIntent.Request) =>
        ServiceRequest.Create(
            "SR-2026-000001",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ServiceCategory.Verification,
            "من فضلكم تحققوا من سند الملكية.",
            UtcNow,
            intent);

    /// <summary>A request the customer has confirmed: what every later transition starts from.</summary>
    private static ServiceRequest CreateUnderReview()
    {
        var request = CreateInquiry();
        request.Confirm(UtcNow);
        return request;
    }

    [Fact(DisplayName = "Create: starts as an inquiry that is not yet a request")]
    public void Create_ValidInputs_StartsAsInquiry()
    {
        var request = CreateInquiry(ServiceRequestIntent.Inquire);

        Assert.Equal(ServiceRequestStatus.Inquiry, request.Status);
        Assert.Equal(ServiceRequestIntent.Inquire, request.Intent);
        Assert.Null(request.ConfirmedAt);
        Assert.Equal(UtcNow, request.LastActivityAt);
        Assert.Equal("SR-2026-000001", request.RequestNumber);
    }

    [Fact(DisplayName = "Confirm: turns the inquiry into a request under review and stamps ConfirmedAt")]
    public void Confirm_FromInquiry_MovesToUnderReview()
    {
        var request = CreateInquiry();
        var later = UtcNow.AddHours(3);

        request.Confirm(later);

        Assert.Equal(ServiceRequestStatus.UnderReview, request.Status);
        Assert.Equal(later, request.ConfirmedAt);
        Assert.Equal(later, request.LastActivityAt);
    }

    [Fact(DisplayName = "Confirm: only once, and never after the request moved on")]
    public void Confirm_Twice_Throws()
    {
        var request = CreateUnderReview();

        Assert.Throws<InvalidStateTransitionException>(() => request.Confirm(UtcNow));
    }

    [Fact(DisplayName = "Accept: an unconfirmed inquiry cannot be accepted")]
    public void Accept_FromInquiry_Throws()
    {
        var request = CreateInquiry();

        Assert.Throws<InvalidStateTransitionException>(() => request.Accept(null, null, null, UtcNow));
    }

    [Fact(DisplayName = "Reject: the provider may decline while it is still an inquiry")]
    public void Reject_FromInquiry_MovesToRejected()
    {
        var request = CreateInquiry();

        request.Reject("لا أغطي هذه المنطقة.", UtcNow, ServiceOutcomeReason.OutsideCoverage);

        Assert.Equal(ServiceRequestStatus.Rejected, request.Status);
        Assert.Null(request.ConfirmedAt);
    }

    [Fact(DisplayName = "Cancel: the customer can withdraw an inquiry")]
    public void Cancel_FromInquiry_MovesToCancelled()
    {
        var request = CreateInquiry();

        request.Cancel(null, UtcNow);

        Assert.Equal(ServiceRequestStatus.Cancelled, request.Status);
        Assert.Null(request.ConfirmedAt);
    }

    [Fact(DisplayName = "RecordMessage: moves the activity clock; closed requests take no messages")]
    public void RecordMessage_TouchesActivity_AndStopsWhenClosed()
    {
        var request = CreateInquiry();
        var later = UtcNow.AddDays(3);

        request.RecordMessage(later);
        Assert.Equal(later, request.LastActivityAt);

        request.Cancel(null, later);
        Assert.False(request.CanExchangeMessages);
        Assert.Throws<InvalidStateTransitionException>(() => request.RecordMessage(later));
    }

    [Theory(DisplayName = "IsConfirmReminderDue: only an unconfirmed inquiry, once, after two days")]
    [InlineData(47, true, false)]
    [InlineData(49, true, true)]
    [InlineData(49, false, false)]
    public void IsConfirmReminderDue_Rules(int hours, bool neverReminded, bool expected)
    {
        var request = CreateInquiry();
        if (!neverReminded) request.MarkReminderSent(UtcNow.AddHours(48));

        Assert.Equal(expected, request.IsConfirmReminderDue(UtcNow.AddHours(hours)));
    }

    [Fact(DisplayName = "IsConfirmReminderDue: a confirmed request is never reminded")]
    public void IsConfirmReminderDue_Confirmed_False()
    {
        var request = CreateUnderReview();

        Assert.False(request.IsConfirmReminderDue(UtcNow.AddDays(5)));
    }

    [Theory(DisplayName = "Expire: stages that expire after a month without contact")]
    [InlineData("inquiry", true)]
    [InlineData("under_review", true)]
    [InlineData("accepted", true)]
    [InlineData("scheduled_past", true)]
    [InlineData("scheduled_future", false)]
    [InlineData("in_progress", false)]
    public void Expire_ByStage(string stage, bool expirable)
    {
        var request = CreateInquiry();
        if (stage != "inquiry") request.Confirm(UtcNow);
        if (stage is "accepted" or "scheduled_past" or "scheduled_future" or "in_progress")
            request.Accept(null, null, null, UtcNow);
        if (stage is "scheduled_past")
            request.Schedule(UtcNow.AddDays(1), UtcNow);
        if (stage is "scheduled_future" or "in_progress")
            request.Schedule(UtcNow.AddDays(60), UtcNow);
        if (stage == "in_progress")
            request.Start(UtcNow);

        var now = UtcNow.AddDays(31);

        Assert.Equal(expirable, request.IsExpirable(now));

        if (expirable)
        {
            request.Expire(ServiceParty.Provider, now);
            Assert.Equal(ServiceRequestStatus.Expired, request.Status);
            Assert.Equal(now, request.ExpiredAt);
            Assert.Equal(ServiceParty.Provider, request.ExpiredWaitingOn);
        }
        else
        {
            Assert.Throws<InvalidStateTransitionException>(() => request.Expire(ServiceParty.Provider, now));
        }
    }

    [Fact(DisplayName = "Expire: a message inside the month keeps the request alive")]
    public void Expire_AfterRecentMessage_NotExpirable()
    {
        var request = CreateInquiry();
        request.RecordMessage(UtcNow.AddDays(20));

        Assert.False(request.IsExpirable(UtcNow.AddDays(31)));
        Assert.True(request.IsExpirable(UtcNow.AddDays(51)));
    }

    [Fact(DisplayName = "Expired is final: no cancel, no messages, no confirm")]
    public void Expired_IsFinal()
    {
        var request = CreateInquiry();
        request.Expire(ServiceParty.Requester, UtcNow.AddDays(31));

        Assert.Throws<InvalidStateTransitionException>(() => request.Cancel(null, UtcNow));
        Assert.Throws<InvalidStateTransitionException>(() => request.RecordMessage(UtcNow));
        Assert.Throws<InvalidStateTransitionException>(() => request.Confirm(UtcNow));
    }

    [Fact(DisplayName = "Create: an unknown intent is refused")]
    public void Create_UnknownIntent_Throws()
    {
        Assert.Throws<DomainException>(() => ServiceRequest.Create(
            "SR-2026-000003", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            ServiceCategory.Verification, null, UtcNow, (ServiceRequestIntent)99));
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

    private static ServiceRequest CreateAccepted()
    {
        var request = CreateUnderReview();
        request.Accept(null, null, null, UtcNow);
        return request;
    }

    [Fact(DisplayName = "Complete: allowed straight from Accepted (Schedule and Start are optional)")]
    public void Complete_FromAccepted_MovesToCompleted()
    {
        var request = CreateAccepted();

        request.Complete(null, null, UtcNow);

        Assert.Equal(ServiceRequestStatus.Completed, request.Status);
    }

    [Fact(DisplayName = "Complete: from UnderReview throws unless done by an admin")]
    public void Complete_FromUnderReview_OnlyAdminMay()
    {
        var request = CreateUnderReview();
        Assert.Throws<InvalidStateTransitionException>(() => request.Complete(null, null, UtcNow));

        request.Complete(null, null, UtcNow, byAdmin: true);

        Assert.Equal(ServiceRequestStatus.Completed, request.Status);
    }

    [Fact(DisplayName = "MarkNotCompleted: stores reason, note and timestamp")]
    public void MarkNotCompleted_FromAccepted_StoresOutcome()
    {
        var request = CreateAccepted();

        request.MarkNotCompleted(ServiceOutcomeReason.PriceNotAgreed, "  السعر مرتفع  ", UtcNow);

        Assert.Equal(ServiceRequestStatus.NotCompleted, request.Status);
        Assert.Equal(ServiceOutcomeReason.PriceNotAgreed, request.OutcomeReason);
        Assert.Equal("السعر مرتفع", request.OutcomeNote);
        Assert.Equal(UtcNow, request.NotCompletedAt);
    }

    [Fact(DisplayName = "MarkNotCompleted: reason 'Other' without a note throws DomainException")]
    public void MarkNotCompleted_OtherWithoutNote_Throws()
    {
        var request = CreateAccepted();

        Assert.Throws<DomainException>(() => request.MarkNotCompleted(ServiceOutcomeReason.Other, " ", UtcNow));
    }

    [Fact(DisplayName = "MarkNotCompleted: unknown reason throws DomainException")]
    public void MarkNotCompleted_UnknownReason_Throws()
    {
        var request = CreateAccepted();

        Assert.Throws<DomainException>(() => request.MarkNotCompleted((ServiceOutcomeReason)1234, null, UtcNow));
    }

    [Fact(DisplayName = "MarkNotCompleted: from UnderReview throws InvalidStateTransitionException")]
    public void MarkNotCompleted_FromUnderReview_Throws()
    {
        var request = CreateUnderReview();

        Assert.Throws<InvalidStateTransitionException>(
            () => request.MarkNotCompleted(ServiceOutcomeReason.CustomerUnreachable, null, UtcNow));
    }

    [Fact(DisplayName = "Cancel: after NotCompleted throws InvalidStateTransitionException")]
    public void Cancel_AfterNotCompleted_Throws()
    {
        var request = CreateAccepted();
        request.MarkNotCompleted(ServiceOutcomeReason.CustomerUnreachable, null, UtcNow);

        Assert.Throws<InvalidStateTransitionException>(() => request.Cancel(null, UtcNow));
    }

    [Fact(DisplayName = "Cancel: stores the requester's structured reason")]
    public void Cancel_StoresRequesterReason()
    {
        var request = CreateUnderReview();

        request.Cancel(null, UtcNow, ServiceRequesterReason.ChangedMind);

        Assert.Equal(ServiceRequesterReason.ChangedMind, request.RequesterReason);
    }

    [Fact(DisplayName = "RecordRequesterFeedback: confirming a completed service needs no reason")]
    public void Feedback_ConfirmCompleted_NoReasonNeeded()
    {
        var request = CreateAccepted();
        request.Complete(null, null, UtcNow);

        request.RecordRequesterFeedback(ServiceRequesterAnswer.Confirmed, null, null, UtcNow);

        Assert.Equal(ServiceRequesterAnswer.Confirmed, request.RequesterAnswer);
        Assert.Null(request.RequesterReason);
        Assert.Equal(UtcNow, request.RequesterFeedbackAt);
    }

    [Fact(DisplayName = "RecordRequesterFeedback: disputing requires a reason")]
    public void Feedback_DisputeWithoutReason_Throws()
    {
        var request = CreateAccepted();
        request.Complete(null, null, UtcNow);

        Assert.Throws<DomainException>(
            () => request.RecordRequesterFeedback(ServiceRequesterAnswer.Disputed, null, null, UtcNow));
    }

    [Fact(DisplayName = "RecordRequesterFeedback: a NotCompleted request requires the customer's reason")]
    public void Feedback_NotCompletedWithoutReason_Throws()
    {
        var request = CreateAccepted();
        request.MarkNotCompleted(ServiceOutcomeReason.ProviderUnavailable, null, UtcNow);

        Assert.Throws<DomainException>(
            () => request.RecordRequesterFeedback(ServiceRequesterAnswer.Confirmed, null, null, UtcNow));

        request.RecordRequesterFeedback(
            ServiceRequesterAnswer.Confirmed, ServiceRequesterReason.ProviderDidNotContact, null, UtcNow);

        Assert.Equal(ServiceRequesterReason.ProviderDidNotContact, request.RequesterReason);
    }

    [Fact(DisplayName = "RecordRequesterFeedback: reason 'Other' needs a note")]
    public void Feedback_OtherWithoutNote_Throws()
    {
        var request = CreateAccepted();
        request.MarkNotCompleted(ServiceOutcomeReason.ProviderUnavailable, null, UtcNow);

        Assert.Throws<DomainException>(() => request.RecordRequesterFeedback(
            ServiceRequesterAnswer.Confirmed, ServiceRequesterReason.Other, null, UtcNow));
    }

    [Fact(DisplayName = "RecordRequesterFeedback: a second answer throws InvalidStateTransitionException")]
    public void Feedback_Twice_Throws()
    {
        var request = CreateAccepted();
        request.Complete(null, null, UtcNow);
        request.RecordRequesterFeedback(ServiceRequesterAnswer.Confirmed, null, null, UtcNow);

        Assert.Throws<InvalidStateTransitionException>(
            () => request.RecordRequesterFeedback(ServiceRequesterAnswer.Confirmed, null, null, UtcNow));
    }

    [Fact(DisplayName = "RecordRequesterFeedback: before the provider closed the request throws")]
    public void Feedback_BeforeClosed_Throws()
    {
        var request = CreateAccepted();

        Assert.Throws<InvalidStateTransitionException>(
            () => request.RecordRequesterFeedback(ServiceRequesterAnswer.Confirmed, null, null, UtcNow));
    }

    [Fact(DisplayName = "RecordRequesterFeedback: still allowed after the review was written")]
    public void Feedback_AfterReviewed_Allowed()
    {
        var request = CreateAccepted();
        request.Complete(null, null, UtcNow);
        request.MarkReviewed(UtcNow);

        request.RecordRequesterFeedback(ServiceRequesterAnswer.Confirmed, null, null, UtcNow);

        Assert.Equal(ServiceRequesterAnswer.Confirmed, request.RequesterAnswer);
    }

    [Fact(DisplayName = "Reject: stores the structured reason code")]
    public void Reject_StoresReasonCode()
    {
        var request = CreateUnderReview();

        request.Reject("خارج منطقتنا.", UtcNow, ServiceOutcomeReason.OutsideCoverage);

        Assert.Equal(ServiceOutcomeReason.OutsideCoverage, request.OutcomeReason);
    }

    [Theory(DisplayName = "ProviderContactVisible: only after acceptance, while the request is alive or delivered")]
    [InlineData("inquiry", false)]
    [InlineData("under_review", false)]
    [InlineData("expired", false)]
    [InlineData("accepted", true)]
    [InlineData("completed", true)]
    [InlineData("rejected", false)]
    [InlineData("cancelled", false)]
    [InlineData("not_completed", false)]
    public void ProviderContactVisible_ByStatus(string state, bool expected)
    {
        var request = state == "inquiry" ? CreateInquiry() : CreateUnderReview();
        switch (state)
        {
            case "expired": request.Expire(ServiceParty.Provider, UtcNow.AddDays(31)); break;
            case "accepted": request.Accept(null, null, null, UtcNow); break;
            case "completed": request.Accept(null, null, null, UtcNow); request.Complete(null, null, UtcNow); break;
            case "rejected": request.Reject("لا.", UtcNow); break;
            case "cancelled": request.Cancel(null, UtcNow); break;
            case "not_completed":
                request.Accept(null, null, null, UtcNow);
                request.MarkNotCompleted(ServiceOutcomeReason.CustomerUnreachable, null, UtcNow);
                break;
        }

        Assert.Equal(expected, request.ProviderContactVisible);
    }
}
