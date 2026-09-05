using Microsoft.Extensions.Logging;
using Moq;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Users.Commands.ChangePassword;
using PropertyApi.Domain.Audit.Constants;
using Xunit;

namespace PropertyApi.Application.Tests.Users;

public sealed class ChangePasswordCommandHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsUserNotFound_WhenIdentityIsMissing()
    {
        // Arrange
        var userId =
            Guid.NewGuid();

        var identity =
            new Mock<IChangePasswordIdentityService>();

        identity
            .Setup(
                x => x.FindByIdAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (IdentityAccountSnapshot?)null);

        var auditLogs =
            new Mock<IAuditLogService>();

        var logger =
            new Mock<
                ILogger<ChangePasswordCommandHandler>>();

        var refreshTokens =
            CreateRefreshTokenMock();

        var sut =
            new ChangePasswordCommandHandler(
                identity.Object,
                auditLogs.Object,
                refreshTokens.Object,
                logger.Object);

        var command =
            new ChangePasswordCommand(
                UserId: userId,
                CurrentPassword: "CurrentPassword123",
                NewPassword: "NewPassword123",
                IpAddress: "127.0.0.1");

        // Act
        var result =
            await sut.Handle(
                command,
                CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.NotFound);
        Assert.Empty(result.Errors);

        identity.Verify(
            x => x.ChangePasswordAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        identity.Verify(
            x => x.RecordCredentialChangeAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        identity.Verify(
            x => x.UpdateSecurityStampAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        VerifyAuditNeverCalled(
            auditLogs);

        refreshTokens.Verify(
            x => x.RevokeActiveTokensForUserAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsUserNotFound_WhenIdentityIsDeleted()
    {
        // Arrange
        var userId =
            Guid.NewGuid();

        var snapshot =
            CreateIdentitySnapshot(
                userId,
                isDeleted: true);

        var identity =
            new Mock<IChangePasswordIdentityService>();

        identity
            .Setup(
                x => x.FindByIdAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        var auditLogs =
            new Mock<IAuditLogService>();

        var logger =
            new Mock<
                ILogger<ChangePasswordCommandHandler>>();

        var refreshTokens =
            CreateRefreshTokenMock();

        var sut =
            new ChangePasswordCommandHandler(
                identity.Object,
                auditLogs.Object,
                refreshTokens.Object,
                logger.Object);

        var command =
            new ChangePasswordCommand(
                UserId: userId,
                CurrentPassword: "CurrentPassword123",
                NewPassword: "NewPassword123",
                IpAddress: "127.0.0.1");

        // Act
        var result =
            await sut.Handle(
                command,
                CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.True(result.NotFound);
        Assert.Empty(result.Errors);

        identity.Verify(
            x => x.ChangePasswordAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        identity.Verify(
            x => x.RecordCredentialChangeAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        identity.Verify(
            x => x.UpdateSecurityStampAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        VerifyAuditNeverCalled(
            auditLogs);

        refreshTokens.Verify(
            x => x.RevokeActiveTokensForUserAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsFailure_WhenChangePasswordFails()
    {
        // Arrange
        var userId =
            Guid.NewGuid();

        var snapshot =
            CreateIdentitySnapshot(userId);

        var expectedErrors =
            new[]
            {
                "Current password is incorrect."
            };

        var identity =
            new Mock<IChangePasswordIdentityService>();

        identity
            .Setup(
                x => x.FindByIdAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        identity
            .Setup(
                x => x.ChangePasswordAsync(
                    userId,
                    "WrongCurrentPassword",
                    "NewPassword123",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Failed(
                    expectedErrors));

        var auditLogs =
            new Mock<IAuditLogService>();

        var logger =
            new Mock<
                ILogger<ChangePasswordCommandHandler>>();

        var refreshTokens =
            CreateRefreshTokenMock();

        var sut =
            new ChangePasswordCommandHandler(
                identity.Object,
                auditLogs.Object,
                refreshTokens.Object,
                logger.Object);

        var command =
            new ChangePasswordCommand(
                UserId: userId,
                CurrentPassword: "WrongCurrentPassword",
                NewPassword: "NewPassword123",
                IpAddress: "127.0.0.1");

        // Act
        var result =
            await sut.Handle(
                command,
                CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.False(result.NotFound);

        Assert.Equal(
            expectedErrors,
            result.Errors);

        identity.Verify(
            x => x.ChangePasswordAsync(
                userId,
                "WrongCurrentPassword",
                "NewPassword123",
                It.IsAny<CancellationToken>()),
            Times.Once);

        identity.Verify(
            x => x.RecordCredentialChangeAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        identity.Verify(
            x => x.UpdateSecurityStampAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        VerifyAuditNeverCalled(
            auditLogs);

        refreshTokens.Verify(
            x => x.RevokeActiveTokensForUserAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        VerifyWarningContains(
            logger,
            "Password change failed");
    }

    [Fact]
    public async Task Handle_ReturnsSuccess_WhenPasswordChangeSucceeds()
    {
        // Arrange
        var userId =
            Guid.NewGuid();

        var identity =
            CreateSuccessfulIdentityMock(
                userId);

        var auditLogs =
            CreateSuccessfulAuditMock();

        var logger =
            new Mock<
                ILogger<ChangePasswordCommandHandler>>();

        var refreshTokens =
            CreateRefreshTokenMock();

        var sut =
            new ChangePasswordCommandHandler(
                identity.Object,
                auditLogs.Object,
                refreshTokens.Object,
                logger.Object);

        var command =
            new ChangePasswordCommand(
                UserId: userId,
                CurrentPassword: "CurrentPassword123",
                NewPassword: "NewPassword123",
                IpAddress: "127.0.0.1");

        // Act
        var result =
            await sut.Handle(
                command,
                CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.NotFound);
        Assert.Empty(result.Errors);

        identity.Verify(
            x => x.ChangePasswordAsync(
                userId,
                "CurrentPassword123",
                "NewPassword123",
                It.IsAny<CancellationToken>()),
            Times.Once);

        identity.Verify(
            x => x.RecordCredentialChangeAsync(
                userId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        identity.Verify(
            x => x.UpdateSecurityStampAsync(
                userId,
                It.IsAny<CancellationToken>()),
            Times.Once);

        refreshTokens.Verify(
            x => x.RevokeActiveTokensForUserAsync(
                userId,
                It.IsAny<DateTime>(),
                "127.0.0.1",
                It.IsAny<CancellationToken>()),
            Times.Once);

        auditLogs.Verify(
            x => x.LogAsync(
                userId,
                AuditActions.ChangePassword,
                "127.0.0.1",
                null,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsSuccess_AndLogsWarning_WhenMetadataUpdateFails()
    {
        // Arrange
        var userId =
            Guid.NewGuid();

        var snapshot =
            CreateIdentitySnapshot(userId);

        var identity =
            new Mock<IChangePasswordIdentityService>();

        identity
            .Setup(
                x => x.FindByIdAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        identity
            .Setup(
                x => x.ChangePasswordAsync(
                    userId,
                    "CurrentPassword123",
                    "NewPassword123",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        identity
            .Setup(
                x => x.RecordCredentialChangeAsync(
                    userId,
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Failed(
                    new[]
                    {
                        "Metadata update failed."
                    }));

        identity
            .Setup(
                x => x.UpdateSecurityStampAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        var auditLogs =
            CreateSuccessfulAuditMock();

        var logger =
            new Mock<
                ILogger<ChangePasswordCommandHandler>>();

        var refreshTokens =
            CreateRefreshTokenMock();

        var sut =
            new ChangePasswordCommandHandler(
                identity.Object,
                auditLogs.Object,
                refreshTokens.Object,
                logger.Object);

        var command =
            new ChangePasswordCommand(
                UserId: userId,
                CurrentPassword: "CurrentPassword123",
                NewPassword: "NewPassword123",
                IpAddress: "127.0.0.1");

        // Act
        var result =
            await sut.Handle(
                command,
                CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.NotFound);

        VerifyWarningContains(
            logger,
            "Identity metadata update failed");

        identity.Verify(
            x => x.UpdateSecurityStampAsync(
                userId,
                It.IsAny<CancellationToken>()),
            Times.Once);

        auditLogs.Verify(
            x => x.LogAsync(
                userId,
                AuditActions.ChangePassword,
                "127.0.0.1",
                null,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsSuccess_AndAuditRecordsFalse_WhenSecurityStampUpdateFails()
    {
        // Arrange
        var userId =
            Guid.NewGuid();

        var snapshot =
            CreateIdentitySnapshot(userId);

        var identity =
            new Mock<IChangePasswordIdentityService>();

        identity
            .Setup(
                x => x.FindByIdAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        identity
            .Setup(
                x => x.ChangePasswordAsync(
                    userId,
                    "CurrentPassword123",
                    "NewPassword123",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        identity
            .Setup(
                x => x.RecordCredentialChangeAsync(
                    userId,
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        identity
            .Setup(
                x => x.UpdateSecurityStampAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Failed(
                    new[]
                    {
                        "Security stamp update failed."
                    }));

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

        var logger =
            new Mock<
                ILogger<ChangePasswordCommandHandler>>();

        var refreshTokens =
            CreateRefreshTokenMock();

        var sut =
            new ChangePasswordCommandHandler(
                identity.Object,
                auditLogs.Object,
                refreshTokens.Object,
                logger.Object);

        var command =
            new ChangePasswordCommand(
                UserId: userId,
                CurrentPassword: "CurrentPassword123",
                NewPassword: "NewPassword123",
                IpAddress: "127.0.0.1");

        // Act
        var result =
            await sut.Handle(
                command,
                CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.False(result.NotFound);

        VerifyWarningContains(
            logger,
            "Security stamp update failed");

        auditLogs.Verify(
            x => x.LogAsync(
                userId,
                AuditActions.ChangePassword,
                "127.0.0.1",
                null,
It.Is<string?>(
    json =>
        json != null &&
        json.Contains(
            "\"securityStampUpdated\":false",
            StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WritesSuccessfulAuditLog_ExactlyOnce()
    {
        // Arrange
        var userId =
            Guid.NewGuid();

        var identity =
            CreateSuccessfulIdentityMock(
                userId);

        var auditLogs =
            CreateSuccessfulAuditMock();

        var logger =
            new Mock<
                ILogger<ChangePasswordCommandHandler>>();

        var refreshTokens =
            CreateRefreshTokenMock();

        var sut =
            new ChangePasswordCommandHandler(
                identity.Object,
                auditLogs.Object,
                refreshTokens.Object,
                logger.Object);

        var command =
            new ChangePasswordCommand(
                UserId: userId,
                CurrentPassword: "CurrentPassword123",
                NewPassword: "NewPassword123",
                IpAddress: "192.168.1.10");

        // Act
        var result =
            await sut.Handle(
                command,
                CancellationToken.None);

        // Assert
        Assert.True(result.Success);

        auditLogs.Verify(
            x => x.LogAsync(
                userId,
                AuditActions.ChangePassword,
                "192.168.1.10",
                null,
It.Is<string?>(
    json =>
        json != null &&
        json.Contains(
            userId.ToString(),
            StringComparison.OrdinalIgnoreCase) &&
        json.Contains(
            "\"securityStampUpdated\":true",
            StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);

        auditLogs.VerifyNoOtherCalls();
    }

    private static Mock<IChangePasswordIdentityService>
        CreateSuccessfulIdentityMock(
            Guid userId)
    {
        var snapshot =
            CreateIdentitySnapshot(userId);

        var identity =
            new Mock<IChangePasswordIdentityService>();

        identity
            .Setup(
                x => x.FindByIdAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);

        identity
            .Setup(
                x => x.ChangePasswordAsync(
                    userId,
                    "CurrentPassword123",
                    "NewPassword123",
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        identity
            .Setup(
                x => x.RecordCredentialChangeAsync(
                    userId,
                    It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        identity
            .Setup(
                x => x.UpdateSecurityStampAsync(
                    userId,
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        return identity;
    }

    private static Mock<IRefreshTokenRepository>
        CreateRefreshTokenMock()
    {
        var refreshTokens =
            new Mock<IRefreshTokenRepository>();

        refreshTokens
            .Setup(
                x => x.RevokeActiveTokensForUserAsync(
                    It.IsAny<Guid>(),
                    It.IsAny<DateTime>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return refreshTokens;
    }

    private static Mock<IAuditLogService>
        CreateSuccessfulAuditMock()
    {
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

        return auditLogs;
    }

    private static IdentityAccountSnapshot
        CreateIdentitySnapshot(
            Guid identityId,
            bool isDeleted = false)
    {
        return new IdentityAccountSnapshot(
            IdentityId:
                identityId,

            UserAccountId:
                identityId,

            Email:
                "user@example.com",

            PhoneNumber:
                null,

            EmailConfirmed:
                true,

            PhoneConfirmed:
                false,

            HasPassword:
                true,

            IsDeleted:
                isDeleted,

            UserName:
                "user@example.com",

            SecurityStamp:
                "security-stamp");
    }

    private static void VerifyAuditNeverCalled(
        Mock<IAuditLogService> auditLogs)
    {
        auditLogs.Verify(
            x => x.LogAsync(
                It.IsAny<Guid?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static void VerifyWarningContains(
        Mock<ILogger<ChangePasswordCommandHandler>>
            logger,
        string expectedMessage)
    {
        logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
It.Is<It.IsAnyType>(
    (state, _) =>
        state.ToString() != null &&
        state.ToString()!.Contains(
            expectedMessage,
            StringComparison.Ordinal)),
                It.IsAny<Exception?>(),
                It.IsAny<
                    Func<
                        It.IsAnyType,
                        Exception?,
                        string>>()),
            Times.Once);
    }
}
