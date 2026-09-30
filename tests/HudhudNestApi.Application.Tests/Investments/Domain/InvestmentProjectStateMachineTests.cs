using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Investments.Entities;
using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Application.Tests.Investments.Domain;

public sealed class InvestmentProjectStateMachineTests
{
    private static InvestmentProject CreateDraft() =>
        InvestmentProject.Create(
            Guid.NewGuid(), Guid.NewGuid(), "مشروع تجريبي", "وصف المشروع", InvestmentProjectType.Residential, "USD");

    [Fact]
    public void Create_StartsAtDraft()
    {
        var project = CreateDraft();
        Assert.Equal(InvestmentProjectStatus.Draft, project.Status);
    }

    [Fact]
    public void Draft_Cannot_Publish_Directly()
    {
        var project = CreateDraft();
        Assert.Throws<DomainException>(() => project.Publish());
    }

    [Fact]
    public void FullHappyPath_ReachesPublished_ThenClosed()
    {
        var project = CreateDraft();

        project.SubmitForReview();
        Assert.Equal(InvestmentProjectStatus.UnderReview, project.Status);

        project.Approve();
        Assert.Equal(InvestmentProjectStatus.Approved, project.Status);

        project.Schedule(null);
        Assert.Equal(InvestmentProjectStatus.Scheduled, project.Status);

        project.Publish();
        Assert.Equal(InvestmentProjectStatus.Published, project.Status);
        Assert.NotNull(project.PublishedAt);

        project.Close();
        Assert.Equal(InvestmentProjectStatus.Closed, project.Status);
        Assert.NotNull(project.ClosedAt);
    }

    [Fact]
    public void Reject_ThenResubmit_ReachesUnderReviewAgain()
    {
        var project = CreateDraft();
        project.SubmitForReview();
        project.Reject("بيانات غير كافية");

        Assert.Equal(InvestmentProjectStatus.Rejected, project.Status);
        Assert.Equal("بيانات غير كافية", project.RejectionReason);

        project.SubmitForReview();
        Assert.Equal(InvestmentProjectStatus.UnderReview, project.Status);
        Assert.Null(project.RejectionReason);
    }

    [Fact]
    public void Reject_Requires_NonEmpty_Reason()
    {
        var project = CreateDraft();
        project.SubmitForReview();

        Assert.Throws<DomainException>(() => project.Reject(""));
    }

    [Fact]
    public void Approve_FromDraft_Throws()
    {
        var project = CreateDraft();
        Assert.Throws<DomainException>(() => project.Approve());
    }

    [Fact]
    public void Suspend_OnlyValid_FromPublished()
    {
        var project = CreateDraft();
        Assert.Throws<DomainException>(() => project.Suspend());

        project.SubmitForReview();
        project.Approve();
        project.Schedule(null);
        project.Publish();

        project.Suspend();
        Assert.Equal(InvestmentProjectStatus.Suspended, project.Status);
    }

    [Fact]
    public void Close_Valid_FromPublished_Or_Suspended()
    {
        var project = CreateDraft();
        project.SubmitForReview();
        project.Approve();
        project.Schedule(null);
        project.Publish();
        project.Suspend();

        project.Close();
        Assert.Equal(InvestmentProjectStatus.Closed, project.Status);
    }

    [Fact]
    public void Schedule_Rejects_PastDate()
    {
        var project = CreateDraft();
        project.SubmitForReview();
        project.Approve();

        Assert.Throws<DomainException>(() => project.Schedule(DateTime.UtcNow.AddDays(-1)));
    }

    [Fact]
    public void UpdateContent_Blocked_Once_UnderReview()
    {
        var project = CreateDraft();
        project.SubmitForReview();

        Assert.Throws<DomainException>(() =>
            project.UpdateContent("عنوان جديد", null, "وصف جديد", InvestmentProjectType.Commercial));
    }

    [Theory]
    [InlineData(0, 100, null)]
    [InlineData(-1, 100, null)]
    public void UpdateInvestmentParameters_Rejects_NonPositive_TargetOrMinimum(decimal target, decimal minimum, decimal? maximum)
    {
        var project = CreateDraft();

        Assert.Throws<DomainException>(() =>
            project.UpdateInvestmentParameters(target, minimum, maximum, "USD", 12, 5, 10));
    }

    [Fact]
    public void UpdateInvestmentParameters_Rejects_MaximumBelowMinimum()
    {
        var project = CreateDraft();

        Assert.Throws<DomainException>(() =>
            project.UpdateInvestmentParameters(10000, 1000, 500, "USD", 12, 5, 10));
    }

    [Fact]
    public void UpdateInvestmentParameters_Rejects_ExpectedReturnMaxBelowMin()
    {
        var project = CreateDraft();

        Assert.Throws<DomainException>(() =>
            project.UpdateInvestmentParameters(10000, 1000, null, "USD", 12, 10, 5));
    }

    [Fact]
    public void UpdateRaisedAmount_Rejects_ExceedingTarget()
    {
        var project = CreateDraft();
        project.UpdateInvestmentParameters(10000, 1000, null, "USD", 12, 5, 10);

        Assert.Throws<DomainException>(() => project.UpdateRaisedAmount(20000));
    }

    [Fact]
    public void UpdateSchedule_Rejects_EndDate_NotAfter_StartDate()
    {
        var project = CreateDraft();
        var start = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Throws<DomainException>(() => project.UpdateSchedule(start, start));
    }
}
