using PropertyApi.Domain.Common.Exceptions;
using PropertyApi.Domain.Investments.Entities;

namespace PropertyApi.Application.Tests.Investments.Domain;

public sealed class InvestmentWatchlistItemTests
{
    [Fact]
    public void Create_Sets_UserId_And_ProjectId()
    {
        var userId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var item = InvestmentWatchlistItem.Create(userId, projectId);

        Assert.Equal(userId, item.UserId);
        Assert.Equal(projectId, item.InvestmentProjectId);
    }

    [Fact]
    public void Create_Rejects_EmptyUserId() =>
        Assert.Throws<DomainException>(() => InvestmentWatchlistItem.Create(Guid.Empty, Guid.NewGuid()));

    [Fact]
    public void Create_Rejects_EmptyProjectId() =>
        Assert.Throws<DomainException>(() => InvestmentWatchlistItem.Create(Guid.NewGuid(), Guid.Empty));
}
