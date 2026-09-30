using Microsoft.Extensions.Logging;
using Moq;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Users.Commands.CancelAccountDeletion;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Application.Tests.Users;

/// <summary>Finding F7 (docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md).</summary>
public sealed class CancelAccountDeletionCommandHandlerTests
{
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_UnknownAccount_ReturnsNotFound()
    {
        var (sut, mocks) = CreateSut();
        var userId = Guid.NewGuid();
        mocks.Accounts
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAccount?)null);

        var result = await sut.Handle(new CancelAccountDeletionCommand(userId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.NotFound);
    }

    [Fact]
    public async Task Handle_AlreadyDeletedAccount_ReturnsNotFound()
    {
        var (sut, mocks) = CreateSut();
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Real", "Name", Now);
        account.RequestDeletion(TimeSpan.FromDays(30), Now);
        account.Anonymize(Now);
        mocks.Accounts
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var result = await sut.Handle(new CancelAccountDeletionCommand(userId), CancellationToken.None);

        Assert.True(result.NotFound);
    }

    [Fact]
    public async Task Handle_NoPendingRequest_ReturnsNoPending()
    {
        var (sut, mocks) = CreateSut();
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Real", "Name", Now);
        mocks.Accounts
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var result = await sut.Handle(new CancelAccountDeletionCommand(userId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.NoPendingRequest);
    }

    [Fact]
    public async Task Handle_PendingRequest_CancelsAndSaves()
    {
        var (sut, mocks) = CreateSut();
        var userId = Guid.NewGuid();
        var account = UserAccount.Create(userId, "Real", "Name", Now);
        account.RequestDeletion(TimeSpan.FromDays(30), Now);
        mocks.Accounts
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var result = await sut.Handle(new CancelAccountDeletionCommand(userId), CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(account.HasPendingDeletionRequest);
        Assert.Null(account.DeletionScheduledFor);
        mocks.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static (CancelAccountDeletionCommandHandler Sut, HandlerMocks Mocks) CreateSut()
    {
        var mocks = new HandlerMocks(
            Accounts: new Mock<IUserAccountRepository>(),
            UnitOfWork: new Mock<IUnitOfWork>());

        mocks.UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var logger = new Mock<ILogger<CancelAccountDeletionCommandHandler>>();

        var sut = new CancelAccountDeletionCommandHandler(
            mocks.Accounts.Object,
            mocks.UnitOfWork.Object,
            logger.Object);

        return (sut, mocks);
    }

    private sealed record HandlerMocks(
        Mock<IUserAccountRepository> Accounts,
        Mock<IUnitOfWork> UnitOfWork);
}
