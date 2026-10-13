using Microsoft.Extensions.Logging;
using Moq;
using HudhudNestApi.Application.Agencies.Interfaces;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Users.Commands.DeleteUser;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Domain.Agencies.Entities;
using HudhudNestApi.Domain.Audit.Constants;
using HudhudNestApi.Domain.Users.Entities;
using Xunit;

namespace HudhudNestApi.Application.Tests.Users;

public sealed class DeleteUserCommandHandlerTests
{
    private const string ValidPassword = "CorrectPassword123";

    [Fact]
    public async Task Handle_ReturnsUserNotFound_WhenIdentityIsMissing()
    {
        var userId = Guid.NewGuid();

        var identity = new Mock<IDeleteUserIdentityService>();
        identity
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityAccountSnapshot?)null);

        var (sut, mocks) = CreateSut(identity);

        var result = await sut.Handle(
            new DeleteUserCommand(userId, ValidPassword, "127.0.0.1"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.NotFound);
        mocks.UnitOfWork.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        mocks.Accounts.Verify(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsUserNotFound_WhenIdentityIsAlreadyDeleted()
    {
        var userId = Guid.NewGuid();

        var identity = new Mock<IDeleteUserIdentityService>();
        identity
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot(userId, hasPassword: true, isDeleted: true));

        var (sut, _) = CreateSut(identity);

        var result = await sut.Handle(
            new DeleteUserCommand(userId, ValidPassword),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.NotFound);
    }

    [Fact]
    public async Task Handle_ReturnsCurrentPasswordRequired_WhenPasswordAccountSubmitsNoPassword()
    {
        var userId = Guid.NewGuid();

        var identity = new Mock<IDeleteUserIdentityService>();
        identity
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot(userId, hasPassword: true));

        var (sut, mocks) = CreateSut(identity);

        var result = await sut.Handle(
            new DeleteUserCommand(userId, CurrentPassword: null),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.NotFound);
        Assert.Contains("CURRENT_PASSWORD_REQUIRED", result.Errors);

        mocks.Login.Verify(
            x => x.VerifyPasswordWithLockoutAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        mocks.UnitOfWork.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsInvalidCurrentPassword_WhenVerificationFails()
    {
        var userId = Guid.NewGuid();

        var identity = new Mock<IDeleteUserIdentityService>();
        identity
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot(userId, hasPassword: true));

        var (sut, mocks) = CreateSut(identity);
        mocks.Login
            .Setup(x => x.VerifyPasswordWithLockoutAsync(userId, "WrongPassword", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginPasswordVerificationResult.InvalidPassword);

        var result = await sut.Handle(
            new DeleteUserCommand(userId, "WrongPassword"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("INVALID_CURRENT_PASSWORD", result.Errors);
        mocks.UnitOfWork.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsAccountLocked_WhenVerificationReportsLockout()
    {
        var userId = Guid.NewGuid();

        var identity = new Mock<IDeleteUserIdentityService>();
        identity
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot(userId, hasPassword: true));

        var (sut, mocks) = CreateSut(identity);
        mocks.Login
            .Setup(x => x.VerifyPasswordWithLockoutAsync(userId, ValidPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginPasswordVerificationResult.LockedOut);

        var result = await sut.Handle(
            new DeleteUserCommand(userId, ValidPassword),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("ACCOUNT_LOCKED", result.Errors);
    }

    [Fact]
    public async Task Handle_SkipsPasswordVerification_ForSocialOnlyAccount()
    {
        var userId = Guid.NewGuid();

        var identity = new Mock<IDeleteUserIdentityService>();
        identity
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot(userId, hasPassword: false));
        identity
            .Setup(x => x.AnonymizeCredentialsAsync(userId, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success());
        identity
            .Setup(x => x.UpdateSecurityStampAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success());
        identity
            .Setup(x => x.SoftDeleteAsync(userId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success());

        var (sut, mocks) = CreateSut(identity);
        mocks.Accounts
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccount(userId));

        var result = await sut.Handle(
            new DeleteUserCommand(userId, CurrentPassword: null, IpAddress: "127.0.0.1"),
            CancellationToken.None);

        Assert.True(result.Success);
        mocks.Login.Verify(
            x => x.VerifyPasswordWithLockoutAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsUserNotFound_WhenUserAccountProfileIsMissing()
    {
        var userId = Guid.NewGuid();

        var identity = new Mock<IDeleteUserIdentityService>();
        identity
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot(userId, hasPassword: true));

        var (sut, mocks) = CreateSut(identity);
        mocks.Login
            .Setup(x => x.VerifyPasswordWithLockoutAsync(userId, ValidPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginPasswordVerificationResult.Success);
        mocks.Accounts
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAccount?)null);

        var result = await sut.Handle(
            new DeleteUserCommand(userId, ValidPassword),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.NotFound);
        mocks.UnitOfWork.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_RollsBack_AndDoesNotSoftDelete_WhenCredentialAnonymizationFails()
    {
        var userId = Guid.NewGuid();

        var identity = new Mock<IDeleteUserIdentityService>();
        identity
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot(userId, hasPassword: true));
        identity
            .Setup(x => x.AnonymizeCredentialsAsync(userId, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Failed("Injected failure."));

        var (sut, mocks) = CreateSut(identity);
        mocks.Login
            .Setup(x => x.VerifyPasswordWithLockoutAsync(userId, ValidPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginPasswordVerificationResult.Success);
        mocks.Accounts
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccount(userId));

        var result = await sut.Handle(
            new DeleteUserCommand(userId, ValidPassword),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Injected failure.", result.Errors);

        identity.Verify(x => x.SoftDeleteAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        mocks.UnitOfWork.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        mocks.UnitOfWork.Verify(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        mocks.RefreshTokens.Verify(
            x => x.RevokeActiveTokensForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_RollsBack_WhenSoftDeleteFails()
    {
        var userId = Guid.NewGuid();

        var identity = new Mock<IDeleteUserIdentityService>();
        identity
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot(userId, hasPassword: true));
        identity
            .Setup(x => x.AnonymizeCredentialsAsync(userId, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success());
        identity
            .Setup(x => x.UpdateSecurityStampAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success());
        identity
            .Setup(x => x.SoftDeleteAsync(userId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Failed("Soft delete blew up."));

        var (sut, mocks) = CreateSut(identity);
        mocks.Login
            .Setup(x => x.VerifyPasswordWithLockoutAsync(userId, ValidPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginPasswordVerificationResult.Success);
        mocks.Accounts
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateAccount(userId));

        var result = await sut.Handle(
            new DeleteUserCommand(userId, ValidPassword),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Soft delete blew up.", result.Errors);
        mocks.UnitOfWork.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        mocks.UnitOfWork.Verify(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AnonymizesAccountProfile_RevokesTokens_DeletesAvatar_AndAudits_OnSuccess()
    {
        var userId = Guid.NewGuid();
        var account = CreateAccount(userId, profileImagePublicId: "user-avatars/old-public-id");

        var identity = SucceedingIdentityMock(userId);
        var (sut, mocks) = CreateSut(identity);
        mocks.Login
            .Setup(x => x.VerifyPasswordWithLockoutAsync(userId, ValidPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginPasswordVerificationResult.Success);
        mocks.Accounts
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var result = await sut.Handle(
            new DeleteUserCommand(userId, ValidPassword, "203.0.113.7"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.NotFound);
        Assert.Empty(result.Errors);

        // Business profile PII is scrubbed, but the row itself still exists (never removed).
        Assert.Equal("Deleted", account.FirstName);
        Assert.Equal("User", account.LastName);
        Assert.Null(account.DisplayName);
        Assert.Null(account.Bio);
        Assert.Null(account.ContactInfo);
        Assert.Null(account.WhatsAppNumber);
        Assert.Null(account.ProfileImageUrl);
        Assert.Null(account.ProfileImagePublicId);

        // Finding F2: Anonymize() also flags the row so AgencyRepository (and any future
        // "primary subject" UserAccounts read) can exclude it -- see UserAccount.IsDeleted's
        // doc comment for why this is a scoped flag, not a global EF Core query filter.
        Assert.True(account.IsDeleted);

        identity.Verify(
            x => x.AnonymizeCredentialsAsync(
                userId,
                It.Is<string>(email => email.StartsWith("deleted-", StringComparison.Ordinal) && email.EndsWith("@deleted.invalid", StringComparison.Ordinal)),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        identity.Verify(x => x.SoftDeleteAsync(userId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);

        mocks.UnitOfWork.Verify(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        mocks.UnitOfWork.Verify(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);

        mocks.RefreshTokens.Verify(
            x => x.RevokeActiveTokensForUserAsync(userId, It.IsAny<DateTime>(), "203.0.113.7", It.IsAny<CancellationToken>()),
            Times.Once);

        mocks.CacheInvalidator.Verify(
            x => x.InvalidateAsync(userId, It.IsAny<CancellationToken>()),
            Times.Once);

        mocks.Storage.Verify(
            x => x.DeleteImageAsync("user-avatars/old-public-id", It.IsAny<CancellationToken>()),
            Times.Once);

        mocks.AuditLogs.Verify(
            x => x.LogAsync(
                userId,
                AuditActions.AccountDeletionCompleted,
                "203.0.113.7",
                null,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_DoesNotCallStorage_WhenNoAvatarWasSet()
    {
        var userId = Guid.NewGuid();
        var account = CreateAccount(userId, profileImagePublicId: null);

        var identity = SucceedingIdentityMock(userId);
        var (sut, mocks) = CreateSut(identity);
        mocks.Login
            .Setup(x => x.VerifyPasswordWithLockoutAsync(userId, ValidPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginPasswordVerificationResult.Success);
        mocks.Accounts
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var result = await sut.Handle(
            new DeleteUserCommand(userId, ValidPassword),
            CancellationToken.None);

        Assert.True(result.Success);
        mocks.Storage.Verify(
            x => x.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_StillSucceeds_WhenAvatarCleanupThrows()
    {
        var userId = Guid.NewGuid();
        var account = CreateAccount(userId, profileImagePublicId: "user-avatars/old-public-id");

        var identity = SucceedingIdentityMock(userId);
        var (sut, mocks) = CreateSut(identity);
        mocks.Login
            .Setup(x => x.VerifyPasswordWithLockoutAsync(userId, ValidPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginPasswordVerificationResult.Success);
        mocks.Accounts
            .Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);
        mocks.Storage
            .Setup(x => x.DeleteImageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cloudinary is down."));

        var result = await sut.Handle(
            new DeleteUserCommand(userId, ValidPassword),
            CancellationToken.None);

        // The account is already, irreversibly, deleted at this point (transaction committed)
        // -- an external-storage hiccup must not surface as a failed deletion.
        Assert.True(result.Success);
    }

    // Owner decision 2026-10-13: a member of an office keeps their listings up and inquiries reach the
    // office owner; anyone else's listings just run out on their normal expiry date.
    private static (UserAccount Member, Agency Agency, UserAccount Owner, HandlerMocks Mocks, DeleteUserCommandHandler Sut)
        ArrangeMember(bool ownerDeleted = false)
    {
        var memberId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var member = CreateAccount(memberId);
        var owner = CreateAccount(ownerId);
        var agency = Agency.Create("Office", "office", ownerId, "SY", DateTime.UtcNow);
        member.JoinAgency(agency.Id, DateTime.UtcNow);
        if (ownerDeleted) owner.Anonymize(DateTime.UtcNow);

        var identity = SucceedingIdentityMock(memberId);
        var (sut, mocks) = CreateSut(identity);
        mocks.Login
            .Setup(x => x.VerifyPasswordWithLockoutAsync(memberId, ValidPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginPasswordVerificationResult.Success);
        mocks.Accounts.Setup(x => x.GetByIdAsync(memberId, It.IsAny<CancellationToken>())).ReturnsAsync(member);
        mocks.Accounts.Setup(x => x.GetByIdAsync(ownerId, It.IsAny<CancellationToken>())).ReturnsAsync(owner);
        mocks.Agencies.Setup(x => x.GetByIdAsync(agency.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agency);
        mocks.Agencies
            .Setup(x => x.TransferListingsAsync(memberId, ownerId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        return (member, agency, owner, mocks, sut);
    }

    [Fact]
    public async Task Handle_AgencyMember_HandsListingsToTheAgencyOwner_InsideTheTransaction_AndAuditsIt()
    {
        var (member, _, owner, mocks, sut) = ArrangeMember();

        var result = await sut.Handle(new DeleteUserCommand(member.Id, ValidPassword), CancellationToken.None);

        Assert.True(result.Success);
        mocks.Agencies.Verify(
            x => x.TransferListingsAsync(member.Id, owner.Id, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        mocks.AuditLogs.Verify(x => x.LogAsync(
            It.IsAny<Guid?>(), AuditActions.AccountDeletionCompleted, It.IsAny<string?>(), It.IsAny<string?>(),
            It.Is<string?>(v => v!.Contains("\"listingsTransferred\":3") && v.Contains(owner.Id.ToString())),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NotAnAgencyMember_TransfersNothing_ListingsRunOutOnTheirOwnExpiry()
    {
        var userId = Guid.NewGuid();
        var identity = SucceedingIdentityMock(userId);
        var (sut, mocks) = CreateSut(identity);
        mocks.Login
            .Setup(x => x.VerifyPasswordWithLockoutAsync(userId, ValidPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginPasswordVerificationResult.Success);
        mocks.Accounts.Setup(x => x.GetByIdAsync(userId, It.IsAny<CancellationToken>())).ReturnsAsync(CreateAccount(userId));

        var result = await sut.Handle(new DeleteUserCommand(userId, ValidPassword), CancellationToken.None);

        Assert.True(result.Success);
        mocks.Agencies.Verify(
            x => x.TransferListingsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_TheAgencyOwnerThemselves_HasNobodyToHandListingsTo()
    {
        var ownerId = Guid.NewGuid();
        var owner = CreateAccount(ownerId);
        var agency = Agency.Create("Office", "office", ownerId, "SY", DateTime.UtcNow);
        owner.JoinAgency(agency.Id, DateTime.UtcNow);

        var (sut, mocks) = CreateSut(SucceedingIdentityMock(ownerId));
        mocks.Login
            .Setup(x => x.VerifyPasswordWithLockoutAsync(ownerId, ValidPassword, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginPasswordVerificationResult.Success);
        mocks.Accounts.Setup(x => x.GetByIdAsync(ownerId, It.IsAny<CancellationToken>())).ReturnsAsync(owner);
        mocks.Agencies.Setup(x => x.GetByIdAsync(agency.Id, It.IsAny<CancellationToken>())).ReturnsAsync(agency);

        var result = await sut.Handle(new DeleteUserCommand(ownerId, ValidPassword), CancellationToken.None);

        Assert.True(result.Success);
        mocks.Agencies.Verify(
            x => x.TransferListingsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_AgencyOwnerAlreadyDeleted_TransfersNothing()
    {
        var (member, _, _, mocks, sut) = ArrangeMember(ownerDeleted: true);

        var result = await sut.Handle(new DeleteUserCommand(member.Id, ValidPassword), CancellationToken.None);

        Assert.True(result.Success);
        mocks.Agencies.Verify(
            x => x.TransferListingsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static (DeleteUserCommandHandler Sut, HandlerMocks Mocks) CreateSut(
        Mock<IDeleteUserIdentityService> identity)
    {
        var mocks = new HandlerMocks(
            Login: new Mock<ILoginIdentityService>(),
            Accounts: new Mock<IUserAccountRepository>(),
            Agencies: new Mock<IAgencyRepository>(),
            RefreshTokens: new Mock<IRefreshTokenRepository>(),
            CacheInvalidator: new Mock<IUserSecurityStampCacheInvalidator>(),
            Storage: new Mock<IMediaStorageService>(),
            UnitOfWork: new Mock<IUnitOfWork>(),
            AuditLogs: new Mock<IAuditLogService>());

        mocks.RefreshTokens
            .Setup(x => x.RevokeActiveTokensForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mocks.CacheInvalidator
            .Setup(x => x.InvalidateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mocks.AuditLogs
            .Setup(x => x.LogAsync(
                It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        mocks.UnitOfWork.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mocks.UnitOfWork.Setup(x => x.CommitTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mocks.UnitOfWork.Setup(x => x.RollbackTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        mocks.UnitOfWork.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var logger = new Mock<ILogger<DeleteUserCommandHandler>>();

        var sut = new DeleteUserCommandHandler(
            identity.Object,
            mocks.Login.Object,
            mocks.Accounts.Object,
            mocks.Agencies.Object,
            mocks.RefreshTokens.Object,
            mocks.CacheInvalidator.Object,
            mocks.Storage.Object,
            mocks.UnitOfWork.Object,
            mocks.AuditLogs.Object,
            logger.Object);

        return (sut, mocks);
    }

    private static Mock<IDeleteUserIdentityService> SucceedingIdentityMock(Guid userId)
    {
        var identity = new Mock<IDeleteUserIdentityService>();

        identity
            .Setup(x => x.FindByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateSnapshot(userId, hasPassword: true));

        identity
            .Setup(x => x.AnonymizeCredentialsAsync(userId, It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success());

        identity
            .Setup(x => x.UpdateSecurityStampAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success());

        identity
            .Setup(x => x.SoftDeleteAsync(userId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success());

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

    private static UserAccount CreateAccount(Guid id, string? profileImagePublicId = null)
    {
        var account = UserAccount.Create(id, "Real", "Name", DateTime.UtcNow);
        if (profileImagePublicId is not null)
        {
            account.UpdateProfileImage(
                "https://res.cloudinary.com/demo/image/upload/user-avatars/old-public-id.jpg",
                profileImagePublicId,
                DateTime.UtcNow);
        }

        return account;
    }

    private sealed record HandlerMocks(
        Mock<ILoginIdentityService> Login,
        Mock<IUserAccountRepository> Accounts,
        Mock<IAgencyRepository> Agencies,
        Mock<IRefreshTokenRepository> RefreshTokens,
        Mock<IUserSecurityStampCacheInvalidator> CacheInvalidator,
        Mock<IMediaStorageService> Storage,
        Mock<IUnitOfWork> UnitOfWork,
        Mock<IAuditLogService> AuditLogs);
}
