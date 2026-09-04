using Moq;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Investments.Commands.AddInvestmentToWatchlist;
using PropertyApi.Application.Investments.DTOs;
using PropertyApi.Application.Investments.Interfaces;
using PropertyApi.Domain.Investments.Entities;

namespace PropertyApi.Application.Tests.Investments;

public sealed class AddInvestmentToWatchlistCommandHandlerTests
{
    [Fact]
    public async Task Add_ReturnsNotFound_WhenProjectDoesNotExist()
    {
        var watchlist = new Mock<IInvestmentWatchlistRepository>();
        watchlist.Setup(x => x.ProjectExistsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var handler = new AddInvestmentToWatchlistCommandHandler(watchlist.Object, Mock.Of<IUnitOfWork>());
        var result = await handler.Handle(new AddInvestmentToWatchlistCommand(Guid.NewGuid(), Guid.NewGuid()), default);

        Assert.Equal(InvestmentWatchlistMutationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Add_ReturnsConflict_WhenAlreadyWatchlisted_AndDoesNotDuplicate()
    {
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var watchlist = new Mock<IInvestmentWatchlistRepository>();
        watchlist.Setup(x => x.ProjectExistsAsync(projectId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        watchlist.Setup(x => x.ExistsAsync(userId, projectId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var handler = new AddInvestmentToWatchlistCommandHandler(watchlist.Object, Mock.Of<IUnitOfWork>());
        var result = await handler.Handle(new AddInvestmentToWatchlistCommand(userId, projectId), default);

        Assert.Equal(InvestmentWatchlistMutationStatus.Conflict, result.Status);
        watchlist.Verify(x => x.Add(It.IsAny<InvestmentWatchlistItem>()), Times.Never);
    }

    [Fact]
    public async Task Add_Succeeds_WhenNotAlreadyWatchlisted()
    {
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var watchlist = new Mock<IInvestmentWatchlistRepository>();
        watchlist.Setup(x => x.ProjectExistsAsync(projectId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        watchlist.Setup(x => x.ExistsAsync(userId, projectId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var handler = new AddInvestmentToWatchlistCommandHandler(watchlist.Object, Mock.Of<IUnitOfWork>());
        var result = await handler.Handle(new AddInvestmentToWatchlistCommand(userId, projectId), default);

        Assert.Equal(InvestmentWatchlistMutationStatus.Success, result.Status);
        watchlist.Verify(x => x.Add(It.IsAny<InvestmentWatchlistItem>()), Times.Once);
    }
}
