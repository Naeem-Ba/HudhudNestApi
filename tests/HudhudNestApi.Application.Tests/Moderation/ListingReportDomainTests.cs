using HudhudNestApi.Application.Moderation;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Moderation;
using HudhudNestApi.Domain.Moderation.Entities;
using HudhudNestApi.Domain.Moderation.Enums;

namespace HudhudNestApi.Application.Tests.Moderation;

public sealed class ListingReportDomainTests
{
    private static readonly DateTime Now = new(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc);

    private static ListingReport New(ListingReportReason reason = ListingReportReason.Fraud, string? details = null) =>
        ListingReport.Submit(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Flat", reason, details, null, Now);

    [Fact]
    public void Submit_StartsNew_WithReasonPriority()
    {
        var r = New(ListingReportReason.Fraud);
        Assert.Equal(ListingReportStatus.New, r.Status);
        Assert.Equal(ListingReportPriority.High, r.Priority);
        Assert.Equal(ListingReportPriority.Low, New(ListingReportReason.Duplicate).Priority);
        Assert.Equal(ListingReportPriority.Normal, New(ListingReportReason.IncorrectDetails).Priority);
    }

    [Fact]
    public void Submit_Rejects_OwnListing_UnknownReason_OtherWithoutDetails_LongDetails()
    {
        var id = Guid.NewGuid();
        Assert.Throws<DomainException>(() => ListingReport.Submit(id, Guid.NewGuid(), id, "t", ListingReportReason.Fraud, null, null, Now));
        Assert.Throws<DomainException>(() => New((ListingReportReason)999));
        Assert.Throws<DomainException>(() => New(ListingReportReason.Other, "short"));
        Assert.Throws<DomainException>(() => New(ListingReportReason.Fraud, new string('x', 2001)));
        Assert.Throws<DomainException>(() => ListingReport.Submit(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), "t", ListingReportReason.Fraud, null, null, Now));
    }

    [Fact]
    public void Close_NeedsNote_AndOutcomeDecidesStatus()
    {
        var a = New();
        Assert.Throws<DomainException>(() => a.Close(ListingReportOutcome.NoViolation, " ", Guid.NewGuid(), Now));
        a.Close(ListingReportOutcome.NoViolation, "fine", Guid.NewGuid(), Now);
        Assert.Equal(ListingReportStatus.Dismissed, a.Status);

        var b = New();
        b.Close(ListingReportOutcome.ListingTakenDown, "removed", Guid.NewGuid(), Now);
        Assert.Equal(ListingReportStatus.Resolved, b.Status);
        Assert.False(b.IsOpen);
        Assert.Throws<InvalidStateTransitionException>(() => b.Close(ListingReportOutcome.NoViolation, "x", Guid.NewGuid(), Now));
        Assert.Throws<InvalidStateTransitionException>(() => b.StartReview(Now));
    }

    [Fact]
    public void Reopen_OnlyWhenClosed_ClearsDecision_CountsReopen()
    {
        var r = New();
        Assert.Throws<InvalidStateTransitionException>(() => r.Reopen(Now));
        r.Close(ListingReportOutcome.OtherAction, "x", Guid.NewGuid(), Now);
        r.Reopen(Now);
        Assert.Equal(ListingReportStatus.UnderReview, r.Status);
        Assert.Null(r.Outcome);
        Assert.Null(r.ResolutionNote);
        Assert.Equal(1, r.ReopenCount);
    }

    [Fact]
    public void ReporterResponse_OnlyByReporter_AndOnlyWhenAsked()
    {
        var r = New();
        Assert.Throws<InvalidStateTransitionException>(() => r.RecordReporterResponse(r.ReporterUserId, Now));
        r.RequestInfo("which photo?", Now);
        Assert.Throws<DomainException>(() => r.RecordReporterResponse(Guid.NewGuid(), Now));
        r.RecordReporterResponse(r.ReporterUserId, Now);
        Assert.Equal(ListingReportStatus.UnderReview, r.Status);
    }

    [Fact]
    public void RaisePriority_NeverLowers()
    {
        var r = New(ListingReportReason.Duplicate);
        Assert.True(r.RaisePriorityTo(ListingReportPriority.High, Now));
        r.ChangePriority(ListingReportPriority.Urgent, Now);
        Assert.False(r.RaisePriorityTo(ListingReportPriority.High, Now));
        Assert.Equal(ListingReportPriority.Urgent, r.Priority);
    }

    [Fact]
    public void Options_ReviewTargets_FallBackWhenInvalid()
    {
        var o = new ListingReportOptions { ReviewTargetHoursUrgent = 0 };
        Assert.Equal(TimeSpan.FromHours(24), o.ReviewTarget(ListingReportPriority.Urgent));
        Assert.Equal(4, o.OverdueCutoffs(Now).Count);
    }

    [Fact]
    public void EveryReason_HasPriority_AndNotificationTexts()
    {
        foreach (var reason in Enum.GetValues<ListingReportReason>())
            Assert.True(Enum.IsDefined(ListingReportPolicy.DefaultPriority(reason)));
    }
}
