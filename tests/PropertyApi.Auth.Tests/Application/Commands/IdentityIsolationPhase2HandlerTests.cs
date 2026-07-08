using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PropertyApi.Application.Auth.Commands.AddEmail;
using PropertyApi.Application.Auth.Commands.VerifyEmail;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;

namespace PropertyApi.Auth.Tests.Application.Commands;

[Trait("Category", "AuthCQRS")]
public sealed class AddEmailCommandHandlerTests
{
    [Fact(
        DisplayName =
            "AddEmail returns USER_NOT_FOUND when identity does not exist")]
    public async Task MissingIdentity_ReturnsFailure()
    {
        // Arrange
        var userId =
            Guid.NewGuid();

        var identity =
            new Mock<IPureIdentityService>();

        identity
            .Setup(
                x => x.FindByIdAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (IdentityAccountSnapshot?)null);

        var emailService =
            new Mock<IEmailVerificationService>();

        var handler =
            new AddEmailCommandHandler(
                identity.Object,
                emailService.Object,
                NullLogger<AddEmailCommandHandler>.Instance);

        // Act
        var result =
            await handler.Handle(
                new AddEmailCommand(
                    userId,
                    "user@example.com"),
                CancellationToken.None);

        // Assert
        Assert.False(result.Success);

        identity.Verify(
            x => x.SetEmailAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        identity.Verify(
            x => x.GenerateEmailConfirmationTokenAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        emailService.Verify(
            x => x.SendVerificationLinkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(
        DisplayName =
            "AddEmail rejects email owned by another identity")]
    public async Task EmailOwnedByAnotherIdentity_ReturnsFailure()
    {
        // Arrange
        var userId =
            Guid.NewGuid();

        var otherIdentityId =
            Guid.NewGuid();

        var currentIdentity =
            CreateIdentitySnapshot(
                identityId: userId,
                email: null,
                emailConfirmed: false);

        var existingIdentity =
            CreateIdentitySnapshot(
                identityId: otherIdentityId,
                email: "taken@example.com",
                emailConfirmed: true);

        var identity =
            new Mock<IPureIdentityService>();

        identity
            .Setup(
                x => x.FindByIdAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                currentIdentity);

        identity
            .Setup(
                x => x.FindByEmailAsync(
                    "taken@example.com",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                existingIdentity);

        var emailService =
            new Mock<IEmailVerificationService>();

        var handler =
            new AddEmailCommandHandler(
                identity.Object,
                emailService.Object,
                NullLogger<AddEmailCommandHandler>.Instance);

        // Act
        var result =
            await handler.Handle(
                new AddEmailCommand(
                    userId,
                    "Taken@Example.com"),
                CancellationToken.None);

        // Assert
        Assert.False(result.Success);

        identity.Verify(
            x => x.FindByEmailAsync(
                "taken@example.com",
                It.IsAny<CancellationToken>()),
            Times.Once);

        identity.Verify(
            x => x.SetEmailAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        identity.Verify(
            x => x.GenerateEmailConfirmationTokenAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        emailService.Verify(
            x => x.SendVerificationLinkAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(
        DisplayName =
            "AddEmail sets normalized email, generates token, and sends verification link")]
    public async Task ValidRequest_SetsEmail_GeneratesToken_AndSendsLink()
    {
        // Arrange
        var userId =
            Guid.NewGuid();

        var currentIdentity =
            CreateIdentitySnapshot(
                identityId: userId,
                email: null,
                emailConfirmed: false);

        var identity =
            new Mock<IPureIdentityService>();

        identity
            .Setup(
                x => x.FindByIdAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                currentIdentity);

        identity
            .Setup(
                x => x.FindByEmailAsync(
                    "user@example.com",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (IdentityAccountSnapshot?)null);

        identity
            .Setup(
                x => x.SetEmailAsync(
                    userId,
                    "user@example.com",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        identity
            .Setup(
                x => x.GenerateEmailConfirmationTokenAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                "confirmation-token");

        var emailService =
            new Mock<IEmailVerificationService>();

        emailService
            .Setup(
                x => x.SendVerificationLinkAsync(
                    "user@example.com",
                    "confirmation-token",
                    It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler =
            new AddEmailCommandHandler(
                identity.Object,
                emailService.Object,
                NullLogger<AddEmailCommandHandler>.Instance);

        // Act
        var result =
            await handler.Handle(
                new AddEmailCommand(
                    userId,
                    " User@Example.com "),
                CancellationToken.None);

        // Assert
        Assert.True(result.Success);

        identity.Verify(
            x => x.FindByEmailAsync(
                "user@example.com",
                It.IsAny<CancellationToken>()),
            Times.Once);

        identity.Verify(
            x => x.SetEmailAsync(
                userId,
                "user@example.com",
                It.IsAny<CancellationToken>()),
            Times.Once);

        identity.Verify(
            x => x.GenerateEmailConfirmationTokenAsync(
                userId,
                It.IsAny<CancellationToken>()),
            Times.Once);

        emailService.Verify(
            x => x.SendVerificationLinkAsync(
                "user@example.com",
                "confirmation-token",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static IdentityAccountSnapshot
        CreateIdentitySnapshot(
            Guid identityId,
            string? email,
            bool emailConfirmed,
            bool isDeleted = false)
        => new(
            IdentityId:
                identityId,

            UserAccountId:
                identityId,

            Email:
                email,

            PhoneNumber:
                null,

            EmailConfirmed:
                emailConfirmed,

            PhoneConfirmed:
                false,

            HasPassword:
                false,

            IsDeleted:
                isDeleted,

            UserName:
                email,

            SecurityStamp:
                "security-stamp");
}

[Trait("Category", "AuthCQRS")]
public sealed class VerifyEmailCommandHandlerTests
{
    [Fact(
        DisplayName =
            "VerifyEmail returns INVALID_TOKEN when identity does not exist")]
    public async Task MissingIdentity_ReturnsInvalidToken()
    {
        // Arrange
        var userId =
            Guid.NewGuid();

        var identity =
            new Mock<IPureIdentityService>();

        identity
            .Setup(
                x => x.FindByIdAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (IdentityAccountSnapshot?)null);

        var handler =
            new VerifyEmailCommandHandler(
                identity.Object,
                NullLogger<VerifyEmailCommandHandler>.Instance);

        // Act
        var result =
            await handler.Handle(
                new VerifyEmailCommand(
                    userId,
                    "confirmation-token"),
                CancellationToken.None);

        // Assert
        Assert.False(result.Success);

        Assert.Equal(
            "INVALID_TOKEN",
            result.ErrorCode);

        identity.Verify(
            x => x.ConfirmEmailAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(
        DisplayName =
            "VerifyEmail returns success when email is already confirmed")]
    public async Task AlreadyConfirmed_ReturnsSuccess_WithoutConfirmCall()
    {
        // Arrange
        var userId =
            Guid.NewGuid();

        var snapshot =
            CreateIdentitySnapshot(
                identityId: userId,
                email: "user@example.com",
                emailConfirmed: true);

        var identity =
            new Mock<IPureIdentityService>();

        identity
            .Setup(
                x => x.FindByIdAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var handler =
            new VerifyEmailCommandHandler(
                identity.Object,
                NullLogger<VerifyEmailCommandHandler>.Instance);

        // Act
        var result =
            await handler.Handle(
                new VerifyEmailCommand(
                    userId,
                    "confirmation-token"),
                CancellationToken.None);

        // Assert
        Assert.True(result.Success);

        identity.Verify(
            x => x.ConfirmEmailAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(
        DisplayName =
            "VerifyEmail returns NO_EMAIL when identity has no email")]
    public async Task IdentityWithoutEmail_ReturnsNoEmail()
    {
        // Arrange
        var userId =
            Guid.NewGuid();

        var snapshot =
            CreateIdentitySnapshot(
                identityId: userId,
                email: null,
                emailConfirmed: false);

        var identity =
            new Mock<IPureIdentityService>();

        identity
            .Setup(
                x => x.FindByIdAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var handler =
            new VerifyEmailCommandHandler(
                identity.Object,
                NullLogger<VerifyEmailCommandHandler>.Instance);

        // Act
        var result =
            await handler.Handle(
                new VerifyEmailCommand(
                    userId,
                    "confirmation-token"),
                CancellationToken.None);

        // Assert
        Assert.False(result.Success);

        Assert.Equal(
            "NO_EMAIL",
            result.ErrorCode);

        identity.Verify(
            x => x.ConfirmEmailAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(
        DisplayName =
            "VerifyEmail returns INVALID_TOKEN when confirmation fails")]
    public async Task InvalidToken_ReturnsFailure()
    {
        // Arrange
        var userId =
            Guid.NewGuid();

        var snapshot =
            CreateIdentitySnapshot(
                identityId: userId,
                email: "user@example.com",
                emailConfirmed: false);

        var identity =
            new Mock<IPureIdentityService>();

        identity
            .Setup(
                x => x.FindByIdAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        identity
            .Setup(
                x => x.ConfirmEmailAsync(
                    userId,
                    "invalid-token",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Failed(
                    new[]
                    {
                        "Invalid token."
                    }));

        var handler =
            new VerifyEmailCommandHandler(
                identity.Object,
                NullLogger<VerifyEmailCommandHandler>.Instance);

        // Act
        var result =
            await handler.Handle(
                new VerifyEmailCommand(
                    userId,
                    "invalid-token"),
                CancellationToken.None);

        // Assert
        Assert.False(result.Success);

        Assert.Equal(
            "INVALID_TOKEN",
            result.ErrorCode);

        identity.Verify(
            x => x.ConfirmEmailAsync(
                userId,
                "invalid-token",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact(
        DisplayName =
            "VerifyEmail confirms email successfully for valid token")]
    public async Task ValidToken_ConfirmsEmail_AndReturnsSuccess()
    {
        // Arrange
        var userId =
            Guid.NewGuid();

        var snapshot =
            CreateIdentitySnapshot(
                identityId: userId,
                email: "user@example.com",
                emailConfirmed: false);

        var identity =
            new Mock<IPureIdentityService>();

        identity
            .Setup(
                x => x.FindByIdAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        identity
            .Setup(
                x => x.ConfirmEmailAsync(
                    userId,
                    "valid-confirmation-token",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        var handler =
            new VerifyEmailCommandHandler(
                identity.Object,
                NullLogger<VerifyEmailCommandHandler>.Instance);

        // Act
        var result =
            await handler.Handle(
                new VerifyEmailCommand(
                    userId,
                    "valid-confirmation-token"),
                CancellationToken.None);

        // Assert
        Assert.True(result.Success);

        Assert.Null(
            result.ErrorCode);

        Assert.Null(
            result.ErrorMessage);

        identity.Verify(
            x => x.ConfirmEmailAsync(
                userId,
                "valid-confirmation-token",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static IdentityAccountSnapshot
        CreateIdentitySnapshot(
            Guid identityId,
            string? email,
            bool emailConfirmed,
            bool isDeleted = false)
        => new(
            IdentityId:
                identityId,

            UserAccountId:
                identityId,

            Email:
                email,

            PhoneNumber:
                null,

            EmailConfirmed:
                emailConfirmed,

            PhoneConfirmed:
                false,

            HasPassword:
                false,

            IsDeleted:
                isDeleted,

            UserName:
                email,

            SecurityStamp:
                "security-stamp");
}