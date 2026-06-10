using Microsoft.Extensions.Logging.Abstractions;
using PropertyApi.Application.Auth.Commands.AddEmail;
using PropertyApi.Application.Auth.Commands.VerifyEmail;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Domain.Users.Constants;
using PropertyApi.Domain.Users.Entities;

namespace PropertyApi.Auth.Tests.Application.Commands;

[Trait("Category", "AuthIdentityIsolation")]
public sealed class AddEmailCommandHandlerTests
{
    [Fact(DisplayName = "AddEmail returns USER_NOT_FOUND when user does not exist")]
    public async Task UserNotFound_ReturnsFail()
    {
        var identityUsers = new Mock<IIdentityUserService>();
        identityUsers.Setup(x => x.FindByIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var handler = new AddEmailCommandHandler(
            identityUsers.Object,
            Mock.Of<IEmailVerificationService>(),
            NullLogger<AddEmailCommandHandler>.Instance);

        var result = await handler.Handle(
            new AddEmailCommand(Guid.NewGuid(), "naeem@example.com"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("USER_NOT_FOUND", result.ErrorCode);
    }

    [Fact(DisplayName = "AddEmail returns EMAIL_TAKEN when another user owns the email")]
    public async Task EmailTaken_ReturnsFail()
    {
        var currentUser = UserBuilder.Valid(id: Guid.NewGuid(), phone: "+491701111111");
        var otherUser = UserBuilder.WithVerifiedEmail("naeem@example.com");

        var identityUsers = new Mock<IIdentityUserService>();
        identityUsers.Setup(x => x.FindByIdAsync(
                currentUser.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(currentUser);

        identityUsers.Setup(x => x.FindByEmailAsync(
                "naeem@example.com",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherUser);

        var handler = new AddEmailCommandHandler(
            identityUsers.Object,
            Mock.Of<IEmailVerificationService>(),
            NullLogger<AddEmailCommandHandler>.Instance);

        var result = await handler.Handle(
            new AddEmailCommand(currentUser.Id, "Naeem@Example.com"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("EMAIL_TAKEN", result.ErrorCode);
    }

    [Fact(DisplayName = "AddEmail sets normalized email and sends verification link")]
    public async Task ValidRequest_SetsEmail_AndSendsVerificationLink()
    {
        var user = UserBuilder.Valid(id: Guid.NewGuid(), phone: "+491701111111");

        var identityUsers = new Mock<IIdentityUserService>();
        identityUsers.Setup(x => x.FindByIdAsync(
                user.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        identityUsers.Setup(x => x.FindByEmailAsync(
                "naeem@example.com",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        identityUsers.Setup(x => x.SetEmailAsync(
                user,
                "naeem@example.com",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success());

        identityUsers.Setup(x => x.GenerateEmailConfirmationTokenAsync(
                user,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("email-token");

        var emailVerification = new Mock<IEmailVerificationService>();

        var handler = new AddEmailCommandHandler(
            identityUsers.Object,
            emailVerification.Object,
            NullLogger<AddEmailCommandHandler>.Instance);

        var result = await handler.Handle(
            new AddEmailCommand(user.Id, "Naeem@Example.com"),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.VerificationSent);

        identityUsers.Verify(x => x.SetEmailAsync(
            user,
            "naeem@example.com",
            It.IsAny<CancellationToken>()), Times.Once);

        emailVerification.Verify(x => x.SendVerificationLinkAsync(
            "naeem@example.com",
            "email-token",
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

[Trait("Category", "AuthIdentityIsolation")]
public sealed class VerifyEmailCommandHandlerTests
{
    [Fact(DisplayName = "VerifyEmail returns generic invalid token when user is missing")]
    public async Task MissingUser_ReturnsInvalidToken()
    {
        var identityUsers = new Mock<IIdentityUserService>();
        identityUsers.Setup(x => x.FindByIdAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var handler = new VerifyEmailCommandHandler(
            identityUsers.Object,
            NullLogger<VerifyEmailCommandHandler>.Instance);

        var result = await handler.Handle(
            new VerifyEmailCommand(Guid.NewGuid(), "valid-looking-token"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("INVALID_TOKEN", result.ErrorCode);
    }

    [Fact(DisplayName = "VerifyEmail returns OK when email is already confirmed")]
    public async Task AlreadyConfirmed_ReturnsOk()
    {
        var user = UserBuilder.WithVerifiedEmail("naeem@example.com");

        var identityUsers = new Mock<IIdentityUserService>();
        identityUsers.Setup(x => x.FindByIdAsync(
                user.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var handler = new VerifyEmailCommandHandler(
            identityUsers.Object,
            NullLogger<VerifyEmailCommandHandler>.Instance);

        var result = await handler.Handle(
            new VerifyEmailCommand(user.Id, "valid-looking-token"),
            CancellationToken.None);

        Assert.True(result.Success);

        identityUsers.Verify(x => x.ConfirmEmailAsync(
            It.IsAny<User>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "VerifyEmail confirms valid token through identity abstraction")]
    public async Task ValidToken_ConfirmsEmail()
    {
        var user = UserBuilder.Valid(
            id: Guid.NewGuid(),
            phone: "+491701111111",
            email: "naeem@example.com",
            emailConfirmed: false);

        var identityUsers = new Mock<IIdentityUserService>();
        identityUsers.Setup(x => x.FindByIdAsync(
                user.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        identityUsers.Setup(x => x.ConfirmEmailAsync(
                user,
                "valid-looking-token",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success());

        var handler = new VerifyEmailCommandHandler(
            identityUsers.Object,
            NullLogger<VerifyEmailCommandHandler>.Instance);

        var result = await handler.Handle(
            new VerifyEmailCommand(user.Id, "valid-looking-token"),
            CancellationToken.None);

        Assert.True(result.Success);

        identityUsers.Verify(x => x.ConfirmEmailAsync(
            user,
            "valid-looking-token",
            It.IsAny<CancellationToken>()), Times.Once);
    }
}

[Trait("Category", "AuthIdentityIsolation")]
public sealed class VerifyPhoneOtpIdentityIsolationTests
{
    [Fact(DisplayName = "IIdentityUserService exposes phone registration operations without UserManager dependency")]
    public void IdentityAbstraction_ContainsPhoneRegistrationOperations()
    {
        var methods = typeof(IIdentityUserService)
            .GetMethods()
            .Select(m => m.Name)
            .ToArray();

        Assert.Contains(nameof(IIdentityUserService.FindByUserNameAsync), methods);
        Assert.Contains(nameof(IIdentityUserService.CreateAsync), methods);
        Assert.Contains(nameof(IIdentityUserService.UpdateAsync), methods);
        Assert.Contains(nameof(IIdentityUserService.AddToRoleAsync), methods);
        Assert.Contains(nameof(IIdentityUserService.GetRolesAsync), methods);
    }
}
