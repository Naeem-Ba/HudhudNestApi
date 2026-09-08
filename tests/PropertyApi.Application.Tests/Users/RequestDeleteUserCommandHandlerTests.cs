using Microsoft.Extensions.Logging;
using Moq;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Users.Commands.RequestDeleteUser;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Application.Tests.Users;

/// <summary>
/// Finding F7 (docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md). Re-authentication branches
/// mirror DeleteUserCommandHandlerTests exactly (same rule, same identity service), since this
/// handler now owns that step for the public "delete my account" entry point.
/// </summary>
public sealed class RequestDeleteUserCommandHandlerTests
{
    private const string ValidPassword = "StrongPass!123";
    private static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Handle_UnknownUser_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var identity = new Mock<IDeleteUserIdentityService>();
        identity
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityAccountSnapshot?)null);

        var (sut, _) = CreateSut(identity);

        var result = await sut.Handle(new RequestDeleteUserCommand(userId), CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.NotFound);
    }

    [Fact]
    public async Task Handle_AlreadyDeletedIdentity_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var identity = new Mock<IDeleteUserIdentityService>();
        identity
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot(userId, hasPassword: true, isDeleted: true));

        var (sut, _) = CreateSut(identity);

        var result = await sut.Handle(new RequestDeleteUserCommand(userId), CancellationToken.None);

        Assert.True(result.NotFound);
    }

    [Fact]
    public async Task Handle_PasswordAccount_NoPasswordSubmitted_ReturnsCurrentPasswordRequired()
    {
        var userId = Guid.NewGuid();
        var identity = SucceedingIdentityMock(userId);
        var (sut, mocks) = CreateSut(identity);

        var result = await sut.Handle(new RequestDeleteUserCommand(userId, null), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("CURRENT_PASSWORD_REQUIRED", result.Errors);
        mocks.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WrongPassword_ReturnsInvalidCurrentPassword()
    {
        var userId = Guid.NewGuid();
        var identity = SucceedingIdentityMock(userId);
        var (sut, mocks) = CreateSut(identity);
        mocks.Login
            .Setup(x => x.VerifyPasswordWithLockoutAsync(userId, "wrong", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginPasswordVerificationResult.InvalidPassword);

        var result = await sut.Handle(new RequestDeleteUserCommand(userId, "wrong"), CancellationToken.None);

        Assert.Contains("INVALID_CURRENT_PASSWORD", result.Errors);
    }

    [Fact]
    public async Task Handle_LockedOutAccount_ReturnsAccountLocked()
    {
        var userId = Guid.NewGuid();
        var identity = SucceedingIdentityMock(userId);
        var (sut, mocks) = CreateSut(identity);
        mocks.Login
            .Setup(x => x.VerifyPasswordWithLockoutAsync(userId, ValidPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginPasswordVerificationResult.LockedOut);

        var result = await sut.Handle(new RequestDeleteUserCommand(userId, ValidPassword), CancellationToken.None);

        Assert.Contains("ACCOUNT_LOCKED", result.Errors);
    }

    [Fact]
    public async Task Handle_SocialLoginOnlyAccount_SkipsPasswordStep()
    {
        var userId = Guid.NewGuid();
        var identity = new Mock<IDeleteUserIdentityService>();
        identity
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot(userId, hasPassword: false));

        var (sut, mocks) = CreateSut(identity);
        var account = UserAccount.Create(userId, "Real", "Name", Now);
        mocks.Accounts
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var result = await sut.Handle(new RequestDeleteUserCommand(userId, null), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.ScheduledFor);
    }

    [Fact]
    public async Task Handle_Success_SchedulesDeletion_WithoutAnonymizing()
    {
        var userId = Guid.NewGuid();
        var identity = SucceedingIdentityMock(userId);
        var (sut, mocks) = CreateSut(identity);
        mocks.Login
            .Setup(x => x.VerifyPasswordWithLockoutAsync(userId, ValidPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginPasswordVerificationResult.Success);

        var account = UserAccount.Create(userId, "Real", "Name", Now);
        mocks.Accounts
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var result = await sut.Handle(new RequestDeleteUserCommand(userId, ValidPassword), CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.ScheduledFor);
        Assert.True(account.HasPendingDeletionRequest);

        // The account is NOT anonymized yet -- that only happens later, via
        // DeleteUserCommandHandler.ExecuteScheduledDeletionAsync, once the sweep picks this up.
        Assert.Equal("Real", account.FirstName);
        Assert.False(account.IsDeleted);

        mocks.UnitOfWork.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        // No transaction/Identity mutation here -- unlike DeleteUserCommandHandler, this is a
        // single-entity save, not a multi-step Identity+UserAccount transaction.
        mocks.UnitOfWork.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MissingUserAccount_ReturnsNotFound()
    {
        var userId = Guid.NewGuid();
        var identity = SucceedingIdentityMock(userId);
        var (sut, mocks) = CreateSut(identity);
        mocks.Login
            .Setup(x => x.VerifyPasswordWithLockoutAsync(userId, ValidPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginPasswordVerificationResult.Success);
        mocks.Accounts
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAccount?)null);

        var result = await sut.Handle(new RequestDeleteUserCommand(userId, ValidPassword), CancellationToken.None);

        Assert.True(result.NotFound);
    }

    private static (RequestDeleteUserCommandHandler Sut, HandlerMocks Mocks) CreateSut(
        Mock<IDeleteUserIdentityService> identity)
    {
        var mocks = new HandlerMocks(
            Login: new Mock<ILoginIdentityService>(),
            Accounts: new Mock<IUserAccountRepository>(),
            UnitOfWork: new Mock<IUnitOfWork>());

        mocks.UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var settings = new Mock<IAccountDeletionSettings>();
        settings.Setup(x => x.DelayDays).Returns(30);

        var logger = new Mock<ILogger<RequestDeleteUserCommandHandler>>();

        var sut = new RequestDeleteUserCommandHandler(
            identity.Object,
            mocks.Login.Object,
            mocks.Accounts.Object,
            settings.Object,
            mocks.UnitOfWork.Object,
            logger.Object);

        return (sut, mocks);
    }

    private static Mock<IDeleteUserIdentityService> SucceedingIdentityMock(Guid userId)
    {
        var identity = new Mock<IDeleteUserIdentityService>();
        identity
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot(userId, hasPassword: true));
        return identity;
    }

    private static IdentityAccountSnapshot CreateSnapshot(
        Guid identityId,
        bool hasPassword,
        bool isDeleted = false)
        => new(
            IdentityId: identityId,
            UserAccountId: identityId,
            Email: "user@example.com",
            PhoneNumber: "+963911234567",
            EmailConfirmed: true,
            PhoneConfirmed: true,
            HasPassword: hasPassword,
            IsDeleted: isDeleted,
            UserName: "user@example.com",
            SecurityStamp: "security-stamp");

    private sealed record HandlerMocks(
        Mock<ILoginIdentityService> Login,
        Mock<IUserAccountRepository> Accounts,
        Mock<IUnitOfWork> UnitOfWork);
}
