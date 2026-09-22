using Moq;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Investments.Commands.ExpressInvestmentInterest;
using HudhudNestApi.Application.Investments.DTOs;
using HudhudNestApi.Application.Investments.Interfaces;
using HudhudNestApi.Domain.Investments.Entities;
using HudhudNestApi.Domain.Investments.Enums;

namespace HudhudNestApi.Application.Tests.Investments;

/// <summary>
/// "أرغب بالاستثمار" must be idempotent (Phase 1 spec §31) and must never succeed against a
/// project that does not exist or is not Published (Phase 1 spec §30).
/// </summary>
public sealed class ExpressInvestmentInterestCommandHandlerTests
{
    [Fact]
    public async Task ExpressInterest_ReturnsNotFound_WhenProjectNotPublished()
    {
        var interests = new Mock<IInvestmentInterestRepository>();
        interests.Setup(x => x.ProjectExistsAndPublishedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var handler = new ExpressInvestmentInterestCommandHandler(
            interests.Object, Mock.Of<IInvestmentProjectRepository>(), Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(new ExpressInvestmentInterestCommand(Guid.NewGuid(), Guid.NewGuid()), default);

        Assert.Equal(InvestmentInterestMutationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task ExpressInterest_FirstTime_CreatesActiveInterest()
    {
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var interests = new Mock<IInvestmentInterestRepository>();
        interests.Setup(x => x.ProjectExistsAndPublishedAsync(projectId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        interests.Setup(x => x.GetAsync(userId, projectId, It.IsAny<CancellationToken>())).ReturnsAsync((InvestmentInterest?)null);

        var handler = new ExpressInvestmentInterestCommandHandler(
            interests.Object, Mock.Of<IInvestmentProjectRepository>(), Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(new ExpressInvestmentInterestCommand(userId, projectId), default);

        Assert.Equal(InvestmentInterestMutationStatus.Success, result.Status);
        Assert.Equal(InvestmentInterestStatus.Active, result.Interest!.Status);
        interests.Verify(x => x.Add(It.IsAny<InvestmentInterest>()), Times.Once);
    }

    [Fact]
    public async Task ExpressInterest_AlreadyActive_ReturnsConflict_WithoutDuplicating()
    {
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var existing = InvestmentInterest.Create(userId, projectId);

        var interests = new Mock<IInvestmentInterestRepository>();
        interests.Setup(x => x.ProjectExistsAndPublishedAsync(projectId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        interests.Setup(x => x.GetAsync(userId, projectId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var handler = new ExpressInvestmentInterestCommandHandler(
            interests.Object, Mock.Of<IInvestmentProjectRepository>(), Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(new ExpressInvestmentInterestCommand(userId, projectId), default);

        Assert.Equal(InvestmentInterestMutationStatus.Conflict, result.Status);
        interests.Verify(x => x.Add(It.IsAny<InvestmentInterest>()), Times.Never);
    }

    [Fact]
    public async Task ExpressInterest_AfterWithdrawal_Reactivates_SameRow()
    {
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var existing = InvestmentInterest.Create(userId, projectId);
        existing.Withdraw();

        var interests = new Mock<IInvestmentInterestRepository>();
        interests.Setup(x => x.ProjectExistsAndPublishedAsync(projectId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        interests.Setup(x => x.GetAsync(userId, projectId, It.IsAny<CancellationToken>())).ReturnsAsync(existing);

        var handler = new ExpressInvestmentInterestCommandHandler(
            interests.Object, Mock.Of<IInvestmentProjectRepository>(), Mock.Of<IUnitOfWork>());

        var result = await handler.Handle(new ExpressInvestmentInterestCommand(userId, projectId), default);

        Assert.Equal(InvestmentInterestMutationStatus.Success, result.Status);
        Assert.Equal(InvestmentInterestStatus.Active, existing.Status);
        interests.Verify(x => x.Add(It.IsAny<InvestmentInterest>()), Times.Never);
    }
}
