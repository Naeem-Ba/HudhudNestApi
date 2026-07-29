using Microsoft.Extensions.Logging.Abstractions;
using PropertyApi.Application.Auth.Commands.VerifyPhoneOtp;
using PropertyApi.Application.Auth.Orchestration;
using PropertyApi.Application.Auth.Policies;
using PropertyApi.Application.Auth.Services;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Auth.Tests.TestHelpers;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Auth.Tests.Application.Commands;

[Trait("Category", "AuthCQRS")]
public sealed class VerifyPhoneOtpCommandHandlerTests
{
    private const string PhoneNumber = "+491701234567";
    private const string OtpPlainText = "123456";
    private const string OtpHash = "hashed-otp";
    private const string IpAddress = "203.0.113.20";

    [Fact(
        DisplayName =
            "VerifyPhoneOtp increments attempts for wrong code without starting authentication transaction")]
    public async Task
        WrongCode_IncrementsAttempts_AndDoesNotStartTransaction()
    {
        var otp =
            OtpCodeEntity();

        var otpRepo =
            new Mock<IOtpCodeRepository>();

        otpRepo
            .Setup(x => x.GetLatestValidAsync(
                PhoneNumber,
                OtpPurpose.PhoneRegistration,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(otp);

        otpRepo
            .Setup(x => x.SaveChangesAsync(
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var otpService =
            new Mock<IOtpService>();

        otpService
            .Setup(x => x.Verify(
                OtpPlainText,
                OtpHash))
            .Returns(false);

        var unitOfWork =
            new Mock<IUnitOfWork>();

        var handler =
            CreateHandler(
                otpRepo,
                otpService,
                new Mock<IPhoneOtpIdentityService>(),
                new Mock<IUserAccountRepository>(),
                new Mock<ITokenService>(),
                new Mock<IRefreshTokenStore>(),
                unitOfWork);

        var result =
            await handler.Handle(
                Command(),
                CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("OTP_WRONG", result.ErrorCode);
        Assert.Equal(1, otp.AttemptCount);

        otpRepo.Verify(x => x.SaveChangesAsync(
            It.IsAny<CancellationToken>()),
            Times.Once);

        unitOfWork.Verify(x => x.BeginTransactionAsync(
            It.IsAny<CancellationToken>()),
            Times.Never);

        unitOfWork.Verify(x => x.CommitTransactionAsync(
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(
        DisplayName =
            "VerifyPhoneOtp signs in an existing phone identity and confirms phone when needed")]
    public async Task
        ExistingIdentity_ConfirmsPhone_PersistsRefreshToken_AndReturnsProfile()
    {
        var identityId =
            Guid.NewGuid();

        var identity =
            IdentitySnapshot(
                identityId,
                phoneConfirmed: false);

        var account =
            UserAccount.Create(
                identityId,
                "Existing",
                "User",
                DateTime.UtcNow);

        var otpRepo =
            CreateSuccessfulOtpRepository();

        var identityService =
            new Mock<IPhoneOtpIdentityService>();

        identityService
            .Setup(x => x.FindByPhoneNumberAsync(
                PhoneNumber,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity);

        identityService
            .Setup(x => x.ConfirmPhoneNumberAsync(
                identityId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        identityService
            .Setup(x => x.GetRolesAsync(
                identityId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { RoleNames.User });

        var accounts =
            new Mock<IUserAccountRepository>();

        accounts
            .Setup(x => x.GetByIdAsync(
                identityId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        var tokenService =
            CreateTokenService(identityId);

        var refreshTokenStore =
            CreateRefreshTokenStore(identityId);

        var unitOfWork =
            CreateUnitOfWork();

        var handler =
            CreateHandler(
                otpRepo,
                SuccessfulOtpService(),
                identityService,
                accounts,
                tokenService,
                refreshTokenStore,
                unitOfWork);

        var result =
            await handler.Handle(
                Command(firstName: null, lastName: null),
                CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.IsNewUser);
        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("refresh-token", result.RefreshToken);
        Assert.Equal(identityId, result.User!.Id);
        Assert.Equal(PhoneNumber, result.User.PhoneNumber);
        Assert.Equal("Existing", result.User.FirstName);

        identityService.Verify(x => x.ConfirmPhoneNumberAsync(
            identityId,
            It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        identityService.Verify(x => x.CreateAsync(
            It.IsAny<CreateIdentityAccount>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        identityService.Verify(x => x.AddToRoleAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        refreshTokenStore.Verify(x => x.StoreAsync(
            identityId,
            "refresh-token",
            IpAddress,
            It.IsAny<CancellationToken>()),
            Times.Once);

        unitOfWork.Verify(x => x.CommitTransactionAsync(
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(
        DisplayName =
            "VerifyPhoneOtp creates a new phone identity with role and profile before issuing tokens")]
    public async Task
        NewPhoneIdentity_CreatesIdentityRoleAndProfile_BeforeIssuingTokens()
    {
        var createdIdentityId =
            Guid.Empty;

        var otpRepo =
            CreateSuccessfulOtpRepository();

        var identityService =
            new Mock<IPhoneOtpIdentityService>();

        identityService
            .Setup(x => x.FindByPhoneNumberAsync(
                PhoneNumber,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityAccountSnapshot?)null);

        identityService
            .Setup(x => x.CreateAsync(
                It.Is<CreateIdentityAccount>(
                    request =>
                        request.PhoneNumber == PhoneNumber &&
                        request.PhoneConfirmed &&
                        request.LegacyFirstName == "Naeem" &&
                        request.LegacyLastName == "Tester"),
                It.IsAny<CancellationToken>()))
            .Callback<CreateIdentityAccount, CancellationToken>(
                (request, _) => createdIdentityId = request.UserAccountId)
            .ReturnsAsync(
                IdentityOperationResult.Success());

        identityService
            .Setup(x => x.AddToRoleAsync(
                It.IsAny<Guid>(),
                RoleNames.User,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        identityService
            .Setup(x => x.FindByIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                () => IdentitySnapshot(
                    createdIdentityId,
                    phoneConfirmed: true));

        identityService
            .Setup(x => x.GetRolesAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { RoleNames.User });

        var accounts =
            new Mock<IUserAccountRepository>();

        accounts
            .Setup(x => x.AddAsync(
                It.IsAny<UserAccount>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tokenService =
            CreateTokenService(() => createdIdentityId);

        var refreshTokenStore =
            CreateRefreshTokenStore(() => createdIdentityId);

        var unitOfWork =
            CreateUnitOfWork();

        var handler =
            CreateHandler(
                otpRepo,
                SuccessfulOtpService(),
                identityService,
                accounts,
                tokenService,
                refreshTokenStore,
                unitOfWork);

        var result =
            await handler.Handle(
                Command(
                    firstName: " Naeem ",
                    lastName: " Tester "),
                CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.IsNewUser);
        Assert.NotEqual(Guid.Empty, createdIdentityId);
        Assert.Equal(createdIdentityId, result.User!.Id);
        Assert.Equal("Naeem", result.User.FirstName);
        Assert.Equal("Tester", result.User.LastName);

        identityService.Verify(x => x.AddToRoleAsync(
            createdIdentityId,
            RoleNames.User,
            It.IsAny<CancellationToken>()),
            Times.Once);

        accounts.Verify(x => x.AddAsync(
            It.Is<UserAccount>(
                account =>
                    account.Id == createdIdentityId &&
                    account.FirstName == "Naeem" &&
                    account.LastName == "Tester"),
            It.IsAny<CancellationToken>()),
            Times.Once);

        unitOfWork.Verify(x => x.SaveChangesAsync(
            It.IsAny<CancellationToken>()),
            Times.Once);

        refreshTokenStore.Verify(x => x.StoreAsync(
            createdIdentityId,
            "refresh-token",
            IpAddress,
            It.IsAny<CancellationToken>()),
            Times.Once);

        unitOfWork.Verify(x => x.CommitTransactionAsync(
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static VerifyPhoneOtpCommandHandler CreateHandler(
        Mock<IOtpCodeRepository> otpRepo,
        Mock<IOtpService> otpService,
        Mock<IPhoneOtpIdentityService> identityService,
        Mock<IUserAccountRepository> accounts,
        Mock<ITokenService> tokenService,
        Mock<IRefreshTokenStore> refreshTokenStore,
        Mock<IUnitOfWork> unitOfWork)
    {
        var sessionIssuer =
            new AuthenticationSessionIssuer(
                new PhoneSessionIdentityAdapter(identityService.Object),
                tokenService.Object,
                new RefreshTokenRepositoryAdapter(refreshTokenStore.Object),
                Mock.Of<IJwtTokenSettings>(),
                Mock.Of<IAuditLogService>(),
                NullLogger<AuthenticationSessionIssuer>.Instance);

        var otp = new OtpConsumptionService(
            otpRepo.Object,
            otpService.Object,
            NullLogger<OtpConsumptionService>.Instance);
        var accountMutations = new PhoneAccountMutationCoordinator(
            identityService.Object,
            accounts.Object,
            new PhoneOwnershipPolicy(),
            unitOfWork.Object,
            NullLogger<PhoneAccountMutationCoordinator>.Instance);
        var orchestrator = new PhoneOtpAuthenticationOrchestrator(
            otp,
            accountMutations,
            sessionIssuer,
            unitOfWork.Object,
            NullLogger<PhoneOtpAuthenticationOrchestrator>.Instance);

        return new VerifyPhoneOtpCommandHandler(orchestrator);
    }

    private static VerifyPhoneOtpCommand Command(
        string? firstName = "Naeem",
        string? lastName = "Tester")
        => new(
            PhoneNumber,
            OtpPlainText,
            OtpPurpose.PhoneRegistration,
            firstName,
            lastName,
            IpAddress);

    private static OtpCode OtpCodeEntity()
        => PropertyApi.Domain.Auth.Entities.OtpCode.Create(
            PhoneNumber,
            OtpHash,
            OtpPurpose.PhoneRegistration,
            IpAddress);

    private static Mock<IOtpCodeRepository> CreateSuccessfulOtpRepository()
    {
        var otp =
            OtpCodeEntity();

        var otpRepo =
            new Mock<IOtpCodeRepository>();

        otpRepo
            .Setup(x => x.GetLatestValidAsync(
                PhoneNumber,
                OtpPurpose.PhoneRegistration,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(otp);

        otpRepo
            .Setup(x => x.TryConsumeAsync(
                otp.Id,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        return otpRepo;
    }

    private static Mock<IOtpService> SuccessfulOtpService()
    {
        var otpService =
            new Mock<IOtpService>();

        otpService
            .Setup(x => x.Verify(
                OtpPlainText,
                OtpHash))
            .Returns(true);

        return otpService;
    }

    private static Mock<IUnitOfWork> CreateUnitOfWork()
    {
        var unitOfWork =
            new Mock<IUnitOfWork>();

        unitOfWork
            .Setup(x => x.BeginTransactionAsync(
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        unitOfWork
            .Setup(x => x.CommitTransactionAsync(
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        unitOfWork
            .Setup(x => x.RollbackTransactionAsync(
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        unitOfWork
            .Setup(x => x.SaveChangesAsync(
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        return unitOfWork;
    }

    private static Mock<ITokenService> CreateTokenService(
        Guid identityId)
        => CreateTokenService(
            () => identityId);

    private static Mock<ITokenService> CreateTokenService(
        Func<Guid> identityId)
    {
        var tokenService =
            new Mock<ITokenService>();

        tokenService
            .Setup(x => x.GenerateAccessToken(
                It.Is<AccessTokenSubject>(
                    subject =>
                        subject.IdentityId == identityId() &&
                        subject.UserName == PhoneNumber),
                It.Is<IReadOnlyCollection<string>>(
                    roles => roles.Contains(RoleNames.User))))
            .Returns("access-token");

        tokenService
            .Setup(x => x.GenerateRefreshToken())
            .Returns("refresh-token");

        tokenService
            .Setup(x => x.GetAccessTokenExpiresAtUtc())
            .Returns(DateTime.UtcNow.AddMinutes(30));

        return tokenService;
    }

    private static Mock<IRefreshTokenStore> CreateRefreshTokenStore(
        Guid identityId)
        => CreateRefreshTokenStore(
            () => identityId);

    private static Mock<IRefreshTokenStore> CreateRefreshTokenStore(
        Func<Guid> identityId)
    {
        var refreshTokenStore =
            new Mock<IRefreshTokenStore>();

        refreshTokenStore
            .Setup(x => x.StoreAsync(
                It.Is<Guid>(id => id == identityId()),
                "refresh-token",
                IpAddress,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return refreshTokenStore;
    }

    private static IdentityAccountSnapshot IdentitySnapshot(
        Guid identityId,
        bool phoneConfirmed)
        => new(
            IdentityId: identityId,
            UserAccountId: identityId,
            Email: null,
            PhoneNumber: PhoneNumber,
            EmailConfirmed: false,
            PhoneConfirmed: phoneConfirmed,
            HasPassword: false,
            IsDeleted: false,
            UserName: PhoneNumber,
            SecurityStamp: $"stamp-{identityId:N}");
}
