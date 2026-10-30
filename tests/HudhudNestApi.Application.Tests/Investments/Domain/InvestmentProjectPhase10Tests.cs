using FluentValidation;
using Moq;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Application.Investments.Commands.UpdateInvestmentProject;
using HudhudNestApi.Application.Investments.Queries.CalculateInvestmentReturn;
using HudhudNestApi.Application.Investments.Queries.GetInvestmentProjects;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Domain.Common.Exceptions;
using HudhudNestApi.Domain.Investments.Entities;
using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Application.Tests.Investments.Domain;

/// <summary>QA Phase 10: the state-machine and validation rules added by the fixes (D03, D04, D06, D07, D09, D12).</summary>
public sealed class InvestmentProjectPhase10Tests
{
    private static InvestmentProject NewProject(InvestmentProjectStatus status)
    {
        var project = InvestmentProject.Create(
            Guid.NewGuid(), Guid.NewGuid(), "مشروع", "وصف", InvestmentProjectType.Residential, "USD");
        project.UpdateInvestmentParameters(100_000m, 1_000m, null, "USD", 12, 5m, 10m);
        if (status != InvestmentProjectStatus.Draft) project.SubmitForReview();
        if (status >= InvestmentProjectStatus.Approved && status != InvestmentProjectStatus.Rejected) project.Approve();
        return project;
    }

    private static InvestmentProject Published()
    {
        var project = NewProject(InvestmentProjectStatus.Approved);
        project.Schedule(null);
        project.Publish();
        return project;
    }

    // ── D04 ──

    [Fact]
    public void Resume_FromSuspended_ReturnsToPublished_KeepingPublishedAt()
    {
        var project = Published();
        var publishedAt = project.PublishedAt;
        project.Suspend();

        project.Resume();

        Assert.Equal(InvestmentProjectStatus.Published, project.Status);
        Assert.Equal(publishedAt, project.PublishedAt);
    }

    [Fact]
    public void Resume_FromPublished_Throws()
    {
        Assert.Throws<DomainException>(() => Published().Resume());
    }

    [Fact]
    public void Resume_FromClosed_Throws()
    {
        var project = Published();
        project.Close();
        Assert.Throws<DomainException>(() => project.Resume());
    }

    // ── D03 ──

    [Fact]
    public void Publish_BeforeTheScheduledTime_Throws()
    {
        var project = NewProject(InvestmentProjectStatus.Approved);
        project.Schedule(DateTime.UtcNow.AddHours(1));

        var ex = Assert.Throws<DomainException>(() => project.Publish());

        Assert.Equal(InvestmentProjectStatus.Scheduled, project.Status);
        Assert.Contains("مجدول", ex.Message);
    }

    [Fact]
    public void Publish_WithoutADate_StaysManual()
    {
        var project = NewProject(InvestmentProjectStatus.Approved);
        project.Schedule(null);

        project.Publish();

        Assert.Equal(InvestmentProjectStatus.Published, project.Status);
    }

    // ── D12 ──

    [Theory]
    [InlineData(InvestmentProjectStatus.Approved)]
    [InlineData(InvestmentProjectStatus.Published)]
    public void UpdateProgress_InLiveStates_Works(InvestmentProjectStatus status)
    {
        var project = status == InvestmentProjectStatus.Published ? Published() : NewProject(status);

        project.UpdateProgress(40_000m);

        Assert.Equal(40_000m, project.RaisedAmount);
    }

    [Fact]
    public void UpdateProgress_Suspended_Works()
    {
        var project = Published();
        project.Suspend();
        project.UpdateProgress(1m);
        Assert.Equal(1m, project.RaisedAmount);
    }

    [Fact]
    public void UpdateProgress_Draft_Throws()
    {
        Assert.Throws<DomainException>(() => NewProject(InvestmentProjectStatus.Draft).UpdateProgress(1m));
    }

    [Fact]
    public void UpdateProgress_Closed_Throws()
    {
        var project = Published();
        project.Close();
        Assert.Throws<DomainException>(() => project.UpdateProgress(1m));
    }

    [Fact]
    public void UpdateProgress_AboveTarget_OrNegative_Throws()
    {
        var project = Published();
        Assert.Throws<DomainException>(() => project.UpdateProgress(100_001m));
        Assert.Throws<DomainException>(() => project.UpdateProgress(-1m));
    }

    // ── D09 (domain side) ──

    [Fact]
    public void UpdateInvestmentParameters_RejectsMinimumAboveTarget_ReturnAbove100_AndAbsurdTerm()
    {
        var project = NewProject(InvestmentProjectStatus.Draft);
        Assert.Throws<DomainException>(() => project.UpdateInvestmentParameters(1_000m, 2_000m, null, "USD", 12, 5m, 10m));
        Assert.Throws<DomainException>(() => project.UpdateInvestmentParameters(100_000m, 1_000m, null, "USD", 12, 5m, 150m));
        Assert.Throws<DomainException>(() => project.UpdateInvestmentParameters(100_000m, 1_000m, null, "USD", 5_000, 5m, 10m));
    }

    // ── validators ──

    private static UpdateInvestmentProjectCommand Update(
        string title = "Valid", string currency = "USD", decimal min = 1_000m, decimal target = 100_000m,
        decimal returnMax = 10m, int term = 12) =>
        new(Guid.NewGuid(), title, "short", "desc", InvestmentProjectType.Residential, target, min, null, currency,
            term, 5m, returnMax, null, null, 0m);

    [Theory]
    [InlineData("guaranteed profit")]
    [InlineData("ربح مؤكد")]
    [InlineData("Your capital is safe")]
    [InlineData("bad\0title")]
    public void UpdateValidator_RefusesTitlesThatPromiseOrCarryNul(string title)
    {
        var result = new UpdateInvestmentProjectCommandValidator().Validate(Update(title: title));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void UpdateValidator_AcceptsAnHonestDisclaimer()
    {
        var command = Update() with { Description = "العائد غير مضمون وقد تخسر جزءًا من رأس المال. Returns are not guaranteed." };
        Assert.True(new UpdateInvestmentProjectCommandValidator().Validate(command).IsValid);
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("ZZZ")]
    public void UpdateValidator_RefusesUnsupportedCurrency(string currency)
    {
        Assert.False(new UpdateInvestmentProjectCommandValidator().Validate(Update(currency: currency)).IsValid);
    }

    [Fact]
    public void UpdateValidator_RefusesMinimumAboveTarget_ReturnAbove100_AndTermOutOfRange()
    {
        var validator = new UpdateInvestmentProjectCommandValidator();
        Assert.False(validator.Validate(Update(min: 200_000m, target: 100_000m)).IsValid);
        Assert.False(validator.Validate(Update(returnMax: 150m)).IsValid);
        Assert.False(validator.Validate(Update(term: 601)).IsValid);
        Assert.True(validator.Validate(Update()).IsValid);
    }

    [Theory]
    [InlineData("1000000000001", 12)]
    [InlineData("1000", 601)]
    public void CalculatorValidator_RefusesAbsurdAmountOrTerm(string amount, int term)
    {
        var query = new CalculateInvestmentReturnQuery(Guid.NewGuid(), decimal.Parse(amount), term);
        Assert.False(new CalculateInvestmentReturnQueryValidator().Validate(query).IsValid);
    }

    [Theory]
    [InlineData(-36500, false)]
    [InlineData(-400, true)]
    [InlineData(30, true)]
    [InlineData(5000, false)]
    public void UpdateValidator_StartDateWindow(int offsetDays, bool valid)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var command = Update() with { StartDate = today.AddDays(offsetDays) };
        Assert.Equal(valid, new UpdateInvestmentProjectCommandValidator().Validate(command).IsValid);
    }

    [Theory]
    [InlineData(500, false)]
    [InlineData(1_000, true)]
    [InlineData(50_000, true)]
    [InlineData(50_001, false)]
    public async Task CalculatorHandler_AppliesProjectMinAndMax(int amount, bool accepted)
    {
        var dto = new InvestmentProjectDetailsDto(
            Guid.NewGuid(), Guid.NewGuid(), "t", null, "d", InvestmentProjectType.Residential,
            InvestmentProjectStatus.Published, "p", "c", null, null, null, null, 100_000m, 0m, 1_000m, 50_000m, "USD",
            12, 5m, 10m, null, null, null, null);
        var repo = new Mock<IInvestmentProjectRepository>();
        repo.Setup(r => r.GetPublishedDetailsAsync(dto.Id, It.IsAny<CancellationToken>())).ReturnsAsync(dto);
        var handler = new CalculateInvestmentReturnQueryHandler(repo.Object);

        var act = () => handler.Handle(new CalculateInvestmentReturnQuery(dto.Id, amount, 12), CancellationToken.None);

        if (accepted) Assert.NotNull(await act());
        else await Assert.ThrowsAsync<HudhudNestApi.Application.Common.Exceptions.ValidationException>(act);
    }

    [Fact]
    public void CalculatorValidator_AcceptsASaneRequest()
    {
        var query = new CalculateInvestmentReturnQuery(Guid.NewGuid(), 5_000m, 12);
        Assert.True(new CalculateInvestmentReturnQueryValidator().Validate(query).IsValid);
    }

    [Fact]
    public void ProjectsQueryValidator_RefusesNulInSearchAndCity()
    {
        var validator = new GetInvestmentProjectsQueryValidator();
        Assert.False(validator.Validate(new GetInvestmentProjectsQuery(new InvestmentProjectFilterDto { Search = "a\0b" })).IsValid);
        Assert.False(validator.Validate(new GetInvestmentProjectsQuery(new InvestmentProjectFilterDto { City = "a\0b" })).IsValid);
        Assert.True(validator.Validate(new GetInvestmentProjectsQuery(new InvestmentProjectFilterDto { Search = "villa" })).IsValid);
    }
}
