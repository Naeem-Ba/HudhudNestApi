using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Application.Auth.Commands.ForgotPassword;
using HudhudNestApi.Application.Auth.Commands.Login;
using HudhudNestApi.Application.Auth.Commands.Register;
using HudhudNestApi.Application.Auth.Commands.ResetPassword;
using HudhudNestApi.Application.Auth.Services;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Users.Interfaces;
using HudhudNestApi.Auth.Tests.TestHelpers;
using HudhudNestApi.Domain.Users.Constants;
using HudhudNestApi.Domain.Users.Entities;

namespace HudhudNestApi.Auth.Tests.Application.Commands;

[Trait("Category", "AuthCQRS")]
public sealed class LoginCommandHandlerTests
{
    [Fact(
        DisplayName =
            "Login succeeds and stores refresh token for valid credentials")]
    public async Task
        ValidCredentials_ReturnsTokens_AndStoresRefreshToken()
    {
        // Arrange
        var user =
            UserBuilder.WithVerifiedEmail(
                "naeem@example.com");

        var identity =
            new IdentityAccountSnapshot(
                IdentityId:
                    user.Id,

                UserAccountId:
                    user.Id,

                Email:
                    user.Email,

                PhoneNumber:
                    user.PhoneNumber,

                EmailConfirmed:
                    user.EmailConfirmed,

                PhoneConfirmed:
                    user.PhoneNumberConfirmed,

                HasPassword:
                    !string.IsNullOrWhiteSpace(
                        user.PasswordHash),

                IsDeleted:
                    user.IsDeleted,

                UserName:
                    user.UserName,

                SecurityStamp:
                    user.SecurityStamp);

        var identityService =
            new Mock<ILoginIdentityService>();

        identityService
            .Setup(
                x => x.FindByEmailAsync(
                    "naeem@example.com",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity);

        identityService
            .Setup(
                x => x.VerifyPasswordWithLockoutAsync(
                    user.Id,
                    "Password123",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginPasswordVerificationResult.Success);

        identityService
            .Setup(
                x => x.GetRolesAsync(
                    user.Id,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new[]
                {
                    RoleNames.User
                });

        identityService
            .Setup(
                x => x.RecordSuccessfulLoginAsync(
                    user.Id,
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        var tokenService =
            new Mock<ITokenService>();

        tokenService
            .Setup(
                x => x.GenerateAccessToken(
                    It.Is<AccessTokenSubject>(
                        subject =>
                            subject.IdentityId ==
                                user.Id &&
                            subject.Email ==
                                user.Email &&
                            subject.UserName ==
                                user.UserName &&
                            subject.SecurityStamp ==
                                user.SecurityStamp),
                    It.Is<
                        IReadOnlyCollection<string>>(
                            roles =>
                                roles.Contains(
                                    RoleNames.User))))
            .Returns("access-token");

        tokenService
            .Setup(
                x => x.GenerateRefreshToken())
            .Returns("refresh-token");

        var refreshRepo =
            new Mock<IRefreshTokenRepository>();

        refreshRepo
            .Setup(
                x => x.AddAsync(
                    user.Id,
                    "refresh-token",
                    "127.0.0.1",
                    It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var jwt =
            new Mock<IJwtTokenSettings>();

        jwt.SetupGet(
                x => x.AccessTokenMinutes)
            .Returns(30);

        var auditLogs =
            new Mock<IAuditLogService>();

        auditLogs
            .Setup(
                x => x.LogAsync(
                    It.IsAny<Guid?>(),
                    It.IsAny<string>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var securityAlerts = new Mock<ISecurityAlertDispatcher>();

        var handler =
            new LoginCommandHandler(
                identityService.Object,
                new AuthenticationSessionIssuer(
                    identityService.Object,
                    tokenService.Object,
                    refreshRepo.Object,
                    jwt.Object,
                    auditLogs.Object,
                    NullLogger<AuthenticationSessionIssuer>.Instance),
                NullLogger<LoginCommandHandler>
                    .Instance,
                securityAlerts.Object);

        // Act
        var result =
            await handler.Handle(
                new LoginCommand(
                    "Naeem@Example.com",
                    "Password123",
                    "127.0.0.1"),
                CancellationToken.None);

        // Assert
        Assert.True(result.Success);

        Assert.Equal(
            "access-token",
            result.AccessToken);

        Assert.Equal(
            "refresh-token",
            result.RefreshToken);

        Assert.Equal(
            1800,
            result.ExpiresIn);

        identityService.Verify(
            x => x.FindByEmailAsync(
                "naeem@example.com",
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Password verification must go through the lockout-aware path; CheckPasswordAsync
        // bypasses AccessFailedCount/LockoutEnd entirely.
        identityService.Verify(
            x => x.VerifyPasswordWithLockoutAsync(
                user.Id,
                "Password123",
                It.IsAny<CancellationToken>()),
            Times.Once);

        identityService.Verify(
            x => x.GetRolesAsync(
                user.Id,
                It.IsAny<CancellationToken>()),
            Times.Once);

        refreshRepo.Verify(
            x => x.AddAsync(
                user.Id,
                "refresh-token",
                "127.0.0.1",
                It.IsAny<CancellationToken>()),
            Times.Once);

        identityService.Verify(
            x => x.RecordSuccessfulLoginAsync(
                user.Id,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        tokenService.Verify(
            x => x.GenerateAccessToken(
                It.IsAny<AccessTokenSubject>(),
                It.IsAny<
                    IReadOnlyCollection<string>>()),
            Times.Once);
    }

    [Fact(
        DisplayName =
            "Login fails for invalid password and does not store refresh token")]
    public async Task
        InvalidPassword_ReturnsUnauthorizedResult()
    {
        // Arrange
        var user =
            UserBuilder.WithVerifiedEmail(
                "naeem@example.com");

        var identity =
            new IdentityAccountSnapshot(
                IdentityId:
                    user.Id,

                UserAccountId:
                    user.Id,

                Email:
                    user.Email,

                PhoneNumber:
                    user.PhoneNumber,

                EmailConfirmed:
                    user.EmailConfirmed,

                PhoneConfirmed:
                    user.PhoneNumberConfirmed,

                HasPassword:
                    !string.IsNullOrWhiteSpace(
                        user.PasswordHash),

                IsDeleted:
                    user.IsDeleted,

                UserName:
                    user.UserName,

                SecurityStamp:
                    user.SecurityStamp);

        var identityService =
            new Mock<ILoginIdentityService>();

        identityService
            .Setup(
                x => x.FindByEmailAsync(
                    "naeem@example.com",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity);

        identityService
            .Setup(
                x => x.VerifyPasswordWithLockoutAsync(
                    user.Id,
                    "wrong",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginPasswordVerificationResult.InvalidPassword);

        var tokenService =
            new Mock<ITokenService>();

        var refreshRepo =
            new Mock<IRefreshTokenRepository>();

        var securityAlerts = new Mock<ISecurityAlertDispatcher>();

        var handler =
            new LoginCommandHandler(
                identityService.Object,
                new AuthenticationSessionIssuer(
                    identityService.Object,
                    tokenService.Object,
                    refreshRepo.Object,
                    Mock.Of<IJwtTokenSettings>(),
                    Mock.Of<IAuditLogService>(),
                    NullLogger<AuthenticationSessionIssuer>.Instance),
                NullLogger<LoginCommandHandler>
                    .Instance,
                securityAlerts.Object);

        // Act
        var result =
            await handler.Handle(
                new LoginCommand(
                    "naeem@example.com",
                    "wrong"),
                CancellationToken.None);

        // Assert
        Assert.False(result.Success);

        Assert.Equal(
            "Invalid credentials.",
            result.Message);

        identityService.Verify(
            x => x.VerifyPasswordWithLockoutAsync(
                user.Id,
                "wrong",
                It.IsAny<CancellationToken>()),
            Times.Once);

        identityService.Verify(
            x => x.GetRolesAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        identityService.Verify(
            x => x.RecordSuccessfulLoginAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        refreshRepo.Verify(
            x => x.AddAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        tokenService.Verify(
            x => x.GenerateAccessToken(
                It.IsAny<AccessTokenSubject>(),
                It.IsAny<
                    IReadOnlyCollection<string>>()),
            Times.Never);
    }
}

[Trait("Category", "AuthCQRS")]
public sealed class RegisterCommandHandlerTests
{
    [Fact(
        DisplayName =
            "Register returns conflict when email already exists")]
    public async Task ExistingEmail_ReturnsConflict()
    {
        // Arrange
        var existingIdentityId =
            Guid.NewGuid();

        var existingIdentity =
            new IdentityAccountSnapshot(
                IdentityId:
                    existingIdentityId,

                UserAccountId:
                    existingIdentityId,

                Email:
                    "naeem@example.com",

                PhoneNumber:
                    null,

                EmailConfirmed:
                    true,

                PhoneConfirmed:
                    false,

                HasPassword:
                    true,

                IsDeleted:
                    false,

                UserName:
                    "naeem@example.com",

                SecurityStamp:
                    "security-stamp");

        var identity =
            new Mock<IRegisterIdentityService>();

        identity
            .Setup(
                x => x.FindByEmailAsync(
                    "naeem@example.com",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                existingIdentity);

        var accounts =
            new Mock<IUserAccountRepository>();

        var unitOfWork =
            Mock.Of<IUnitOfWork>();

        var handler =
            new RegisterCommandHandler(
                identity.Object,
                accounts.Object,
                unitOfWork,
                Mock.Of<IEmailVerificationService>(),
                Mock.Of<IConsentRecordRepository>(),
                NullLogger<RegisterCommandHandler>.Instance);

        // Act
        var result =
            await handler.Handle(
                new RegisterCommand(
                    "Naeem",
                    "Bazzazeh",
                    "naeem@example.com",
                    "Password123"),
                CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.Conflict);

        Assert.Equal(
            "Email already registered.",
            result.Message);

        Assert.Null(result.UserId);

        identity.Verify(
            x => x.CreateAsync(
                It.IsAny<CreateIdentityAccount>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        identity.Verify(
            x => x.AddToRoleAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(
        DisplayName =
            "Register creates identity and assigns User role")]
    public async Task NewEmail_CreatesIdentity_AndAssignsRole()
    {
        // Arrange
        var identity =
            new Mock<IRegisterIdentityService>();

        identity
            .Setup(
                x => x.FindByEmailAsync(
                    "naeem@example.com",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (IdentityAccountSnapshot?)null);

        identity
            .Setup(
                x => x.CreateAsync(
                    It.IsAny<CreateIdentityAccount>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        identity
            .Setup(
                x => x.AddToRoleAsync(
                    It.IsAny<Guid>(),
                    RoleNames.User,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        identity
            .Setup(
                x => x.GenerateEmailConfirmationTokenAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                "confirmation-token");

        var accounts =
            new Mock<IUserAccountRepository>();

        var unitOfWork =
            Mock.Of<IUnitOfWork>();

        var emailVerification =
            new Mock<IEmailVerificationService>();

        var handler =
            new RegisterCommandHandler(
                identity.Object,
                accounts.Object,
                unitOfWork,
                emailVerification.Object,
                Mock.Of<IConsentRecordRepository>(),
                NullLogger<RegisterCommandHandler>.Instance);

        // Act
        var result =
            await handler.Handle(
                new RegisterCommand(
                    "Naeem",
                    "Bazzazeh",
                    "Naeem@Example.com",
                    "Password123"),
                CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.Conflict);
        Assert.NotNull(result.UserId);

        var createdUserId =
            result.UserId!.Value;

        identity.Verify(
            x => x.FindByEmailAsync(
                "naeem@example.com",
                It.IsAny<CancellationToken>()),
            Times.Once);

        identity.Verify(
            x => x.CreateAsync(
                It.Is<CreateIdentityAccount>(
                    request =>
                        request.UserAccountId ==
                            createdUserId &&

                        request.Email ==
                            "naeem@example.com" &&

                        request.PhoneNumber ==
                            null &&

                        request.Password ==
                            "Password123" &&

                        request.LegacyFirstName ==
                            "Naeem" &&

                        request.LegacyLastName ==
                            "Bazzazeh" &&

                        request.LegacyCreatedAtUtc
                            != null),
                It.IsAny<CancellationToken>()),
            Times.Once);

        identity.Verify(
            x => x.AddToRoleAsync(
                createdUserId,
                RoleNames.User,
                It.IsAny<CancellationToken>()),
            Times.Once);

        // The link goes to the normalized address, not the mixed-case one typed in.
        emailVerification.Verify(
            x => x.SendVerificationLinkAsync(
                "naeem@example.com",
                "confirmation-token",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(
        DisplayName =
            "Register succeeds even when the confirmation email cannot be sent")]
    public async Task MailFailure_StillReturnsSuccess()
    {
        // Arrange
        //
        // Reporting a committed registration as a failure is the worst outcome on
        // offer: the user tries again and collides with the email they just claimed
        // seconds earlier. A refused SMTP connection leaves an unconfirmed account,
        // which is exactly what an unconfirmed account is for.
        var identity =
            new Mock<IRegisterIdentityService>();

        identity
            .Setup(
                x => x.FindByEmailAsync(
                    "naeem@example.com",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (IdentityAccountSnapshot?)null);

        identity
            .Setup(
                x => x.CreateAsync(
                    It.IsAny<CreateIdentityAccount>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        identity
            .Setup(
                x => x.AddToRoleAsync(
                    It.IsAny<Guid>(),
                    RoleNames.User,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        identity
            .Setup(
                x => x.GenerateEmailConfirmationTokenAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                "confirmation-token");

        var emailVerification =
            new Mock<IEmailVerificationService>();

        emailVerification
            .Setup(
                x => x.SendVerificationLinkAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
            .ThrowsAsync(
                new InvalidOperationException(
                    "SMTP host refused the connection."));

        var handler =
            new RegisterCommandHandler(
                identity.Object,
                new Mock<IUserAccountRepository>().Object,
                Mock.Of<IUnitOfWork>(),
                emailVerification.Object,
                Mock.Of<IConsentRecordRepository>(),
                NullLogger<RegisterCommandHandler>.Instance);

        // Act
        var result =
            await handler.Handle(
                new RegisterCommand(
                    "Naeem",
                    "Bazzazeh",
                    "naeem@example.com",
                    "Password123"),
                CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.UserId);

        emailVerification.Verify(
            x => x.SendVerificationLinkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(
        DisplayName =
            "Losing the unique-index race for an email is a conflict and mails nothing")]
    public async Task DuplicateEmailRace_ReturnsConflict_AndSendsNoEmail()
    {
        var identity =
            new Mock<IRegisterIdentityService>();

        identity
            .Setup(
                x => x.FindByEmailAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityAccountSnapshot?)null);

        identity
            .Setup(
                x => x.CreateAsync(
                    It.IsAny<CreateIdentityAccount>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Failed(
                    IdentityOperationResult.DuplicateEmailCode));

        var emailVerification =
            new Mock<IEmailVerificationService>();

        var handler =
            new RegisterCommandHandler(
                identity.Object,
                new Mock<IUserAccountRepository>().Object,
                Mock.Of<IUnitOfWork>(),
                emailVerification.Object,
                Mock.Of<IConsentRecordRepository>(),
                NullLogger<RegisterCommandHandler>.Instance);

        var result =
            await handler.Handle(
                new RegisterCommand(
                    "Naeem",
                    "Bazzazeh",
                    "  Naeem@Example.COM ",
                    "Password123"),
                CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.Conflict);

        emailVerification.Verify(
            x => x.SendVerificationLinkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

[Trait("Category", "AuthCQRS")]
public sealed class ForgotPasswordCommandHandlerTests
{
    [Fact(
        DisplayName =
            "ForgotPassword does not reveal whether unknown email exists")]
    public async Task
        UnknownEmail_ReturnsGenericSuccess_AndDoesNotSendEmail()
    {
        var identityService =
            new Mock<IForgotPasswordIdentityService>();

        identityService
            .Setup(
                x => x.FindByEmailAsync(
                    "missing@example.com",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (IdentityAccountSnapshot?)null);

        var emailSender =
            new Mock<IApplicationEmailSender>();

        var handler =
            new ForgotPasswordCommandHandler(
                identityService.Object,
                Mock.Of<IUserAccountRepository>(),
                emailSender.Object,
                Mock.Of<IPasswordResetUrlBuilder>(),
                NullLogger<ForgotPasswordCommandHandler>
                    .Instance);

        var result =
            await handler.Handle(
                new ForgotPasswordCommand(
                    "missing@example.com",
                    "https",
                    "localhost"),
                CancellationToken.None);

        Assert.True(result.Success);

        Assert.Contains(
            "If the email is registered",
            result.Message);

        emailSender.Verify(
            x => x.SendEmailAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact(
        DisplayName =
            "ForgotPassword sends reset email for valid active identity")]
    public async Task ExistingActiveIdentity_SendsEmail()
    {
        var id =
            Guid.NewGuid();

        var identity =
            new IdentityAccountSnapshot(
                IdentityId: id,
                UserAccountId: id,
                Email: "naeem@example.com",
                PhoneNumber: null,
                EmailConfirmed: true,
                PhoneConfirmed: false,
                HasPassword: true,
                IsDeleted: false,
                UserName: "naeem@example.com",
                SecurityStamp: "stamp");

        var identityService =
            new Mock<IForgotPasswordIdentityService>();

        identityService
            .Setup(
                x => x.FindByEmailAsync(
                    "naeem@example.com",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity);

        identityService
            .Setup(
                x => x.GeneratePasswordResetTokenAsync(
                    id,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync("reset-token");

        var userAccounts =
            new Mock<IUserAccountRepository>();

        userAccounts
            .Setup(
                x => x.GetByIdAsync(
                    id,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (UserAccount?)null);

        var emailSender =
            new Mock<IApplicationEmailSender>();

        var urlBuilder =
            new Mock<IPasswordResetUrlBuilder>();

        urlBuilder
            .Setup(
                x => x.Build(
                    "naeem@example.com",
                    "reset-token",
                    "https",
                    "localhost"))
            .Returns(
                "https://localhost/reset-password?email=naeem%40example.com&token=reset-token");

        var handler =
            new ForgotPasswordCommandHandler(
                identityService.Object,
                userAccounts.Object,
                emailSender.Object,
                urlBuilder.Object,
                NullLogger<ForgotPasswordCommandHandler>
                    .Instance);

        var result =
            await handler.Handle(
                new ForgotPasswordCommand(
                    "naeem@example.com",
                    "https",
                    "localhost"),
                CancellationToken.None);

        Assert.True(result.Success);

        identityService.Verify(
            x => x.GeneratePasswordResetTokenAsync(
                id,
                It.IsAny<CancellationToken>()),
            Times.Once);

        emailSender.Verify(
            x => x.SendEmailAsync(
                "naeem@example.com",
                "Reset your password",
                It.Is<string>(
                    body =>
                        body.Contains(
                            "Reset your password"))),
            Times.Once);
    }
}

[Trait("Category", "AuthCQRS")]
public sealed class ResetPasswordCommandHandlerTests
{
    [Fact(
        DisplayName =
            "ResetPassword rejects same password")]
    public async Task SamePassword_ReturnsConflict()
    {
        var id =
            Guid.NewGuid();

        var identity =
            CreateIdentitySnapshot(id);

        var identityService =
            new Mock<IResetPasswordIdentityService>();

        identityService
            .Setup(
                x => x.FindByEmailAsync(
                    "naeem@example.com",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity);

        identityService
            .Setup(
                x => x.CheckPasswordAsync(
                    id,
                    "Password123",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler =
            new ResetPasswordCommandHandler(
                identityService.Object,
                Mock.Of<IRefreshTokenRepository>(),
                NullLogger<ResetPasswordCommandHandler>
                    .Instance);

        var result =
            await handler.Handle(
                new ResetPasswordCommand(
                    "naeem@example.com",
                    "token",
                    "Password123",
                    "Password123",
                    "127.0.0.1"),
                CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.Conflict);

        Assert.Equal(
            "New password must be different from the current password.",
            result.Message);

        identityService.Verify(
            x => x.ResetPasswordAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(
        DisplayName =
            "ResetPassword resets password, updates security stamp, records credential change, and revokes active tokens")]
    public async Task
        ValidRequest_ResetsPassword_AndRevokesTokens()
    {
        var id =
            Guid.NewGuid();

        var identity =
            CreateIdentitySnapshot(id);

        var identityService =
            new Mock<IResetPasswordIdentityService>();

        identityService
            .Setup(
                x => x.FindByEmailAsync(
                    "naeem@example.com",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity);

        identityService
            .Setup(
                x => x.CheckPasswordAsync(
                    id,
                    "NewPassword123",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        identityService
            .Setup(
                x => x.ResetPasswordAsync(
                    id,
                    "token",
                    "NewPassword123",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        identityService
            .Setup(
                x => x.UpdateSecurityStampAsync(
                    id,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        identityService
            .Setup(
                x => x.RecordCredentialChangeAsync(
                    id,
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        var refreshRepo =
            new Mock<IRefreshTokenRepository>();

        var handler =
            new ResetPasswordCommandHandler(
                identityService.Object,
                refreshRepo.Object,
                NullLogger<ResetPasswordCommandHandler>
                    .Instance);

        var result =
            await handler.Handle(
                new ResetPasswordCommand(
                    "naeem@example.com",
                    "token",
                    "NewPassword123",
                    "NewPassword123",
                    "127.0.0.1"),
                CancellationToken.None);

        Assert.True(result.Success);

        identityService.Verify(
            x => x.ResetPasswordAsync(
                id,
                "token",
                "NewPassword123",
                It.IsAny<CancellationToken>()),
            Times.Once);

        identityService.Verify(
            x => x.UpdateSecurityStampAsync(
                id,
                It.IsAny<CancellationToken>()),
            Times.Once);

        identityService.Verify(
            x => x.RecordCredentialChangeAsync(
                id,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        refreshRepo.Verify(
            x => x.RevokeActiveTokensForUserAsync(
                id,
                It.IsAny<DateTime>(),
                "127.0.0.1",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static IdentityAccountSnapshot
        CreateIdentitySnapshot(
            Guid id)
        => new(
            IdentityId: id,
            UserAccountId: id,
            Email: "naeem@example.com",
            PhoneNumber: null,
            EmailConfirmed: true,
            PhoneConfirmed: false,
            HasPassword: true,
            IsDeleted: false,
            UserName: "naeem@example.com",
            SecurityStamp: "stamp");
}
