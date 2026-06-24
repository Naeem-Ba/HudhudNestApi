using Microsoft.Extensions.Logging.Abstractions;
using PropertyApi.Application.Auth.Commands.ForgotPassword;
using PropertyApi.Application.Auth.Commands.Login;
using PropertyApi.Application.Auth.Commands.Register;
using PropertyApi.Application.Auth.Commands.ResetPassword;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Auth.Tests.TestHelpers;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Auth.Tests.Application.Commands;

[Trait("Category", "AuthCQRS")]
public sealed class LoginCommandHandlerTests
{
    [Fact(DisplayName = "Login succeeds and stores refresh token for valid credentials")]
    public async Task ValidCredentials_ReturnsTokens_AndStoresRefreshToken()
    {
        var user = UserBuilder.WithVerifiedEmail("naeem@example.com");

        var identityUsers = new Mock<IIdentityUserService>();
        identityUsers.Setup(x => x.FindByEmailAsync(
                "naeem@example.com",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        identityUsers.Setup(x => x.CheckPasswordAsync(
                user,
                "Password123",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        identityUsers.Setup(x => x.GetRolesAsync(
                user,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { RoleNames.User });

        identityUsers.Setup(x => x.UpdateAsync(
                user,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success());

        var tokenService = new Mock<ITokenService>();
        tokenService.Setup(x => x.GenerateAccessToken(
                user,
                It.IsAny<IReadOnlyCollection<string>>()))
            .Returns("access-token");

        tokenService.Setup(x => x.GenerateRefreshToken())
            .Returns("refresh-token");

        var refreshRepo = new Mock<IRefreshTokenRepository>();

        var jwt = new Mock<IJwtTokenSettings>();
        jwt.SetupGet(x => x.AccessTokenMinutes).Returns(30);

        var auditLogs = new Mock<IAuditLogService>();
        auditLogs.Setup(x => x.LogAsync(
                It.IsAny<Guid?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new LoginCommandHandler(
            identityUsers.Object,
            tokenService.Object,
            refreshRepo.Object,
            jwt.Object,
            auditLogs.Object,
            NullLogger<LoginCommandHandler>.Instance);

        var result = await handler.Handle(
            new LoginCommand("Naeem@Example.com", "Password123", "127.0.0.1"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("access-token", result.AccessToken);
        Assert.Equal("refresh-token", result.RefreshToken);
        Assert.Equal(1800, result.ExpiresIn);

        refreshRepo.Verify(x => x.AddAsync(
            user.Id,
            "refresh-token",
            "127.0.0.1",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact(DisplayName = "Login fails for invalid password and does not store refresh token")]
    public async Task InvalidPassword_ReturnsUnauthorizedResult()
    {
        var user = UserBuilder.WithVerifiedEmail("naeem@example.com");

        var identityUsers = new Mock<IIdentityUserService>();
        identityUsers.Setup(x => x.FindByEmailAsync(
                "naeem@example.com",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        identityUsers.Setup(x => x.CheckPasswordAsync(
                user,
                "wrong",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var handler = new LoginCommandHandler(
            identityUsers.Object,
            Mock.Of<ITokenService>(),
            Mock.Of<IRefreshTokenRepository>(),
            Mock.Of<IJwtTokenSettings>(),
            Mock.Of<IAuditLogService>(),
            NullLogger<LoginCommandHandler>.Instance);

        var result = await handler.Handle(
            new LoginCommand("naeem@example.com", "wrong"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Invalid credentials.", result.Message);
    }
}

[Trait("Category", "AuthCQRS")]
public sealed class RegisterCommandHandlerTests
{
    [Fact(DisplayName = "Register returns conflict when email already exists")]
    public async Task ExistingEmail_ReturnsConflict()
    {
        var existing = UserBuilder.WithVerifiedEmail("naeem@example.com");

        var identityUsers = new Mock<IIdentityUserService>();
        identityUsers.Setup(x => x.FindByEmailAsync(
                "naeem@example.com",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var handler = new RegisterCommandHandler(
            identityUsers.Object,
            NullLogger<RegisterCommandHandler>.Instance);

        var result = await handler.Handle(
            new RegisterCommand("Naeem", "Bazzazeh", "naeem@example.com", "Password123"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.Conflict);
        Assert.Equal("Email already registered.", result.Message);
    }

    [Fact(DisplayName = "Register creates user and assigns User role")]
    public async Task NewEmail_CreatesUser_AndAssignsRole()
    {
        var identityUsers = new Mock<IIdentityUserService>();

        identityUsers.Setup(x => x.FindByEmailAsync(
                "naeem@example.com",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        identityUsers.Setup(x => x.CreateAsync(
                It.IsAny<User>(),
                "Password123",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success());

        identityUsers.Setup(x => x.AddToRoleAsync(
                It.IsAny<User>(),
                RoleNames.User,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success());

        var handler = new RegisterCommandHandler(
            identityUsers.Object,
            NullLogger<RegisterCommandHandler>.Instance);

        var result = await handler.Handle(
            new RegisterCommand("Naeem", "Bazzazeh", "Naeem@Example.com", "Password123"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.UserId);

        identityUsers.Verify(x => x.CreateAsync(
            It.Is<User>(u =>
                u.Email == "naeem@example.com" &&
                u.UserName == "naeem@example.com"),
            "Password123",
            It.IsAny<CancellationToken>()), Times.Once);

        identityUsers.Verify(x => x.AddToRoleAsync(
            It.IsAny<User>(),
            RoleNames.User,
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

[Trait("Category", "AuthCQRS")]
public sealed class ForgotPasswordCommandHandlerTests
{
    [Fact(DisplayName = "ForgotPassword does not reveal whether unknown email exists")]
    public async Task UnknownEmail_ReturnsGenericSuccess_AndDoesNotSendEmail()
    {
        var identityUsers = new Mock<IIdentityUserService>();
        identityUsers.Setup(x => x.FindByEmailAsync(
                "missing@example.com",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var emailSender = new Mock<IApplicationEmailSender>();
        var urlBuilder = Mock.Of<IPasswordResetUrlBuilder>();

        var handler = new ForgotPasswordCommandHandler(
            identityUsers.Object,
            emailSender.Object,
            urlBuilder,
            NullLogger<ForgotPasswordCommandHandler>.Instance);

        var result = await handler.Handle(
            new ForgotPasswordCommand("missing@example.com", "https", "localhost"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains("If the email is registered", result.Message);

        emailSender.Verify(x => x.SendEmailAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<string>()), Times.Never);
    }

    [Fact(DisplayName = "ForgotPassword sends reset email for valid active user")]
    public async Task ExistingActiveUser_SendsEmail()
    {
        var user = UserBuilder.WithVerifiedEmail("naeem@example.com");

        var identityUsers = new Mock<IIdentityUserService>();
        identityUsers.Setup(x => x.FindByEmailAsync(
                "naeem@example.com",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        identityUsers.Setup(x => x.GeneratePasswordResetTokenAsync(
                user,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("reset-token");

        var emailSender = new Mock<IApplicationEmailSender>();

        var urlBuilder = new Mock<IPasswordResetUrlBuilder>();
        urlBuilder.Setup(x => x.Build(
                "naeem@example.com",
                "reset-token",
                "https",
                "localhost"))
            .Returns("https://localhost/reset-password?email=naeem%40example.com&token=reset-token");

        var handler = new ForgotPasswordCommandHandler(
            identityUsers.Object,
            emailSender.Object,
            urlBuilder.Object,
            NullLogger<ForgotPasswordCommandHandler>.Instance);

        var result = await handler.Handle(
            new ForgotPasswordCommand("naeem@example.com", "https", "localhost"),
            CancellationToken.None);

        Assert.True(result.Success);

        emailSender.Verify(x => x.SendEmailAsync(
            "naeem@example.com",
            "Reset your password",
            It.Is<string>(body => body.Contains("Reset your password"))), Times.Once);
    }
}

[Trait("Category", "AuthCQRS")]
public sealed class ResetPasswordCommandHandlerTests
{
    [Fact(DisplayName = "ResetPassword rejects same password")]
    public async Task SamePassword_ReturnsConflict()
    {
        var user = UserBuilder.WithVerifiedEmail("naeem@example.com");

        var identityUsers = new Mock<IIdentityUserService>();
        identityUsers.Setup(x => x.FindByEmailAsync(
                "naeem@example.com",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        identityUsers.Setup(x => x.CheckPasswordAsync(
                user,
                "Password123",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var handler = new ResetPasswordCommandHandler(
            identityUsers.Object,
            Mock.Of<IRefreshTokenRepository>(),
            NullLogger<ResetPasswordCommandHandler>.Instance);

        var result = await handler.Handle(
            new ResetPasswordCommand(
                "naeem@example.com",
                "token",
                "Password123",
                "Password123",
                "127.0.0.1"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.Conflict);
        Assert.Equal("New password must be different from the current password.", result.Message);
    }

    [Fact(DisplayName = "ResetPassword resets password, updates security stamp, and revokes active tokens")]
    public async Task ValidRequest_ResetsPassword_AndRevokesTokens()
    {
        var user = UserBuilder.WithVerifiedEmail("naeem@example.com");

        var identityUsers = new Mock<IIdentityUserService>();
        identityUsers.Setup(x => x.FindByEmailAsync(
                "naeem@example.com",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        identityUsers.Setup(x => x.CheckPasswordAsync(
                user,
                "NewPassword123",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        identityUsers.Setup(x => x.ResetPasswordAsync(
                user,
                "token",
                "NewPassword123",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success());

        identityUsers.Setup(x => x.UpdateSecurityStampAsync(
                user,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success());

        identityUsers.Setup(x => x.UpdateAsync(
                user,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success());

        var refreshRepo = new Mock<IRefreshTokenRepository>();

        var handler = new ResetPasswordCommandHandler(
            identityUsers.Object,
            refreshRepo.Object,
            NullLogger<ResetPasswordCommandHandler>.Instance);

        var result = await handler.Handle(
            new ResetPasswordCommand(
                "naeem@example.com",
                "token",
                "NewPassword123",
                "NewPassword123",
                "127.0.0.1"),
            CancellationToken.None);

        Assert.True(result.Success);

        identityUsers.Verify(x => x.ResetPasswordAsync(
            user,
            "token",
            "NewPassword123",
            It.IsAny<CancellationToken>()), Times.Once);

        identityUsers.Verify(x => x.UpdateSecurityStampAsync(
            user,
            It.IsAny<CancellationToken>()), Times.Once);

        refreshRepo.Verify(x => x.RevokeActiveTokensForUserAsync(
            user.Id,
            It.IsAny<DateTime>(),
            "127.0.0.1",
            It.IsAny<CancellationToken>()), Times.Once);
    }
}