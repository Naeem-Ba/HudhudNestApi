using Microsoft.Extensions.Logging.Abstractions;
using PropertyApi.Application.Auth.Commands.Logout;
using PropertyApi.Application.Auth.Commands.RefreshToken;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Audit.Constants;
using PropertyApi.Domain.Users.Constants;

namespace PropertyApi.Auth.Tests.Application.Commands;

[Trait("Category", "AuthCQRS")]
public sealed class RefreshTokenCommandHandlerTests
{
    private const string ExistingRefreshToken =
        "existing-refresh-token";

    private const string NewRefreshToken =
        "new-refresh-token";

    private const string IpAddress =
        "203.0.113.10";

    [Fact(
        DisplayName =
            "RefreshToken rotates an active token and issues a new access token")]
    public async Task
        ActiveToken_RotatesRefreshToken_AndIssuesAccessToken()
    {
        var userId =
            Guid.NewGuid();

        var tokenId =
            Guid.NewGuid();

        var stored =
            new RefreshTokenRecord(
                tokenId,
                userId,
                DateTime.UtcNow.AddHours(1),
                IsRevoked: false);

        var identity =
            CreateIdentitySnapshot(userId);

        var refreshTokens =
            CreateTransactionalRefreshTokenRepository();

        refreshTokens
            .Setup(x => x.GetByRefreshTokenAsync(
                ExistingRefreshToken,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        refreshTokens
            .Setup(x => x.RevokeIfActiveAsync(
                tokenId,
                It.IsAny<DateTime>(),
                IpAddress,
                NewRefreshToken,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        refreshTokens
            .Setup(x => x.AddAsync(
                userId,
                NewRefreshToken,
                IpAddress,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var identityService =
            new Mock<IRefreshTokenIdentityService>();

        identityService
            .Setup(x => x.FindByIdAsync(
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity);

        identityService
            .Setup(x => x.GetRolesAsync(
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { RoleNames.User });

        var tokenService =
            new Mock<ITokenService>();

        tokenService
            .Setup(x => x.GenerateRefreshToken())
            .Returns(NewRefreshToken);

        tokenService
            .Setup(x => x.GenerateAccessToken(
                It.Is<AccessTokenSubject>(
                    subject =>
                        subject.IdentityId == userId &&
                        subject.Email == identity.Email &&
                        subject.UserName == identity.UserName &&
                        subject.SecurityStamp == identity.SecurityStamp),
                It.Is<IReadOnlyCollection<string>>(
                    roles => roles.Contains(RoleNames.User))))
            .Returns("new-access-token");

        var jwtSettings =
            new Mock<IJwtTokenSettings>();

        jwtSettings
            .SetupGet(x => x.AccessTokenMinutes)
            .Returns(30);

        var cacheInvalidator =
            new Mock<IUserSecurityStampCacheInvalidator>();

        var auditLogs =
            new Mock<IAuditLogService>();

        var handler =
            new RefreshTokenCommandHandler(
                refreshTokens.Object,
                identityService.Object,
                tokenService.Object,
                jwtSettings.Object,
                CreateReuseHandler(
                    refreshTokens,
                    identityService,
                    cacheInvalidator,
                    auditLogs),
                NullLogger<RefreshTokenCommandHandler>.Instance);

        var result =
            await handler.Handle(
                new RefreshTokenCommand(
                    ExistingRefreshToken,
                    IpAddress),
                CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("new-access-token", result.AccessToken);
        Assert.Equal(NewRefreshToken, result.RefreshToken);
        Assert.Equal(1800, result.ExpiresIn);

        refreshTokens.Verify(x => x.RevokeIfActiveAsync(
            tokenId,
            It.IsAny<DateTime>(),
            IpAddress,
            NewRefreshToken,
            It.IsAny<CancellationToken>()),
            Times.Once);

        refreshTokens.Verify(x => x.AddAsync(
            userId,
            NewRefreshToken,
            IpAddress,
            It.IsAny<CancellationToken>()),
            Times.Once);

        refreshTokens.Verify(x => x.RevokeActiveTokensForUserAsync(
            It.IsAny<Guid>(),
            It.IsAny<DateTime>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        cacheInvalidator.Verify(x => x.InvalidateAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        auditLogs.Verify(x => x.LogAsync(
            It.IsAny<Guid?>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact(
        DisplayName =
            "RefreshToken detects reuse of a revoked token and revokes all active sessions")]
    public async Task
        RevokedToken_RevokesActiveSessions_UpdatesStamp_InvalidatesCache_AndAuditsReuse()
    {
        var userId =
            Guid.NewGuid();

        var stored =
            new RefreshTokenRecord(
                Guid.NewGuid(),
                userId,
                DateTime.UtcNow.AddHours(1),
                IsRevoked: true);

        var refreshTokens =
            CreateTransactionalRefreshTokenRepository();

        refreshTokens
            .Setup(x => x.GetByRefreshTokenAsync(
                ExistingRefreshToken,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        refreshTokens
            .Setup(x => x.RevokeActiveTokensForUserAsync(
                userId,
                It.IsAny<DateTime>(),
                IpAddress,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var identityService =
            CreateIdentityServiceForReuseHandling(userId);

        var cacheInvalidator =
            new Mock<IUserSecurityStampCacheInvalidator>();

        cacheInvalidator
            .Setup(x => x.InvalidateAsync(
                userId,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var auditLogs =
            CreateAuditLogService();

        var tokenService =
            new Mock<ITokenService>();

        var handler =
            CreateRefreshHandler(
                refreshTokens,
                identityService,
                tokenService,
                cacheInvalidator,
                auditLogs);

        var result =
            await handler.Handle(
                new RefreshTokenCommand(
                    ExistingRefreshToken,
                    IpAddress),
                CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(
            "Refresh token reuse detected. All active sessions were revoked.",
            result.Message);

        VerifyReuseHandling(
            refreshTokens,
            identityService,
            cacheInvalidator,
            auditLogs,
            userId);

        refreshTokens.Verify(x => x.RevokeIfActiveAsync(
            It.IsAny<Guid>(),
            It.IsAny<DateTime>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        tokenService.Verify(x => x.GenerateRefreshToken(), Times.Never);
        tokenService.Verify(x => x.GenerateAccessToken(
            It.IsAny<AccessTokenSubject>(),
            It.IsAny<IReadOnlyCollection<string>>()),
            Times.Never);
    }

    [Fact(
        DisplayName =
            "RefreshToken treats failed active-token revocation as a concurrent rotation")]
    public async Task
        RevokeIfActiveReturnsFalse_DoesNotRevokeTheWinningConcurrentSession()
    {
        var userId =
            Guid.NewGuid();

        var tokenId =
            Guid.NewGuid();

        var stored =
            new RefreshTokenRecord(
                tokenId,
                userId,
                DateTime.UtcNow.AddHours(1),
                IsRevoked: false);

        var refreshTokens =
            CreateTransactionalRefreshTokenRepository();

        refreshTokens
            .Setup(x => x.GetByRefreshTokenAsync(
                ExistingRefreshToken,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        refreshTokens
            .Setup(x => x.RevokeIfActiveAsync(
                tokenId,
                It.IsAny<DateTime>(),
                IpAddress,
                NewRefreshToken,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        refreshTokens
            .Setup(x => x.RevokeActiveTokensForUserAsync(
                userId,
                It.IsAny<DateTime>(),
                IpAddress,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var identityService =
            CreateIdentityServiceForReuseHandling(userId);

        var cacheInvalidator =
            new Mock<IUserSecurityStampCacheInvalidator>();

        cacheInvalidator
            .Setup(x => x.InvalidateAsync(
                userId,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var auditLogs =
            CreateAuditLogService();

        var tokenService =
            new Mock<ITokenService>();

        tokenService
            .Setup(x => x.GenerateRefreshToken())
            .Returns(NewRefreshToken);

        var handler =
            CreateRefreshHandler(
                refreshTokens,
                identityService,
                tokenService,
                cacheInvalidator,
                auditLogs);

        var result =
            await handler.Handle(
                new RefreshTokenCommand(
                    ExistingRefreshToken,
                    IpAddress),
                CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(
            "Refresh token was already rotated by another request.",
            result.Message);

        refreshTokens.Verify(x => x.RevokeActiveTokensForUserAsync(
            It.IsAny<Guid>(),
            It.IsAny<DateTime>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        cacheInvalidator.Verify(x => x.InvalidateAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        auditLogs.Verify(x => x.LogAsync(
            It.IsAny<Guid?>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);

        refreshTokens.Verify(x => x.AddAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        tokenService.Verify(x => x.GenerateAccessToken(
            It.IsAny<AccessTokenSubject>(),
            It.IsAny<IReadOnlyCollection<string>>()),
            Times.Never);
    }

    private static RefreshTokenCommandHandler CreateRefreshHandler(
        Mock<IRefreshTokenRepository> refreshTokens,
        Mock<IRefreshTokenIdentityService> identityService,
        Mock<ITokenService> tokenService,
        Mock<IUserSecurityStampCacheInvalidator> cacheInvalidator,
        Mock<IAuditLogService> auditLogs)
    {
        var jwtSettings =
            new Mock<IJwtTokenSettings>();

        jwtSettings
            .SetupGet(x => x.AccessTokenMinutes)
            .Returns(30);

        return new RefreshTokenCommandHandler(
            refreshTokens.Object,
            identityService.Object,
            tokenService.Object,
            jwtSettings.Object,
            CreateReuseHandler(
                refreshTokens,
                identityService,
                cacheInvalidator,
                auditLogs),
            NullLogger<RefreshTokenCommandHandler>.Instance);
    }

    private static RefreshTokenReuseHandler CreateReuseHandler(
        Mock<IRefreshTokenRepository> refreshTokens,
        Mock<IRefreshTokenIdentityService> identityService,
        Mock<IUserSecurityStampCacheInvalidator> cacheInvalidator,
        Mock<IAuditLogService> auditLogs)
        => new(
            refreshTokens.Object,
            identityService.Object,
            cacheInvalidator.Object,
            auditLogs.Object,
            NullLogger<RefreshTokenReuseHandler>.Instance);

    private static Mock<IRefreshTokenRepository>
        CreateTransactionalRefreshTokenRepository()
    {
        var refreshTokens =
            new Mock<IRefreshTokenRepository>();

        refreshTokens
            .Setup(x => x.ExecuteInTransactionAsync(
                It.IsAny<Func<CancellationToken, Task<RefreshTokenResult>>>(),
                It.IsAny<CancellationToken>()))
            .Returns(
                (Func<CancellationToken, Task<RefreshTokenResult>> action,
                    CancellationToken ct) =>
                    action(ct));

        return refreshTokens;
    }

    private static Mock<IRefreshTokenIdentityService>
        CreateIdentityServiceForReuseHandling(
            Guid userId)
    {
        var identityService =
            new Mock<IRefreshTokenIdentityService>();

        identityService
            .Setup(x => x.FindByIdAsync(
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                CreateIdentitySnapshot(userId));

        identityService
            .Setup(x => x.UpdateSecurityStampAsync(
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        return identityService;
    }

    private static Mock<IAuditLogService> CreateAuditLogService()
    {
        var auditLogs =
            new Mock<IAuditLogService>();

        auditLogs
            .Setup(x => x.LogAsync(
                It.IsAny<Guid?>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return auditLogs;
    }

    private static void VerifyReuseHandling(
        Mock<IRefreshTokenRepository> refreshTokens,
        Mock<IRefreshTokenIdentityService> identityService,
        Mock<IUserSecurityStampCacheInvalidator> cacheInvalidator,
        Mock<IAuditLogService> auditLogs,
        Guid userId)
    {
        refreshTokens.Verify(x => x.RevokeActiveTokensForUserAsync(
            userId,
            It.IsAny<DateTime>(),
            IpAddress,
            It.IsAny<CancellationToken>()),
            Times.Once);

        identityService.Verify(x => x.UpdateSecurityStampAsync(
            userId,
            It.IsAny<CancellationToken>()),
            Times.Once);

        cacheInvalidator.Verify(x => x.InvalidateAsync(
            userId,
            It.IsAny<CancellationToken>()),
            Times.Once);

        auditLogs.Verify(x => x.LogAsync(
            userId,
            AuditActions.RefreshTokenReuseDetected,
            IpAddress,
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static IdentityAccountSnapshot CreateIdentitySnapshot(
        Guid userId)
        => new(
            IdentityId: userId,
            UserAccountId: userId,
            Email: "naeem@example.com",
            PhoneNumber: "+963944000000",
            EmailConfirmed: true,
            PhoneConfirmed: true,
            HasPassword: true,
            IsDeleted: false,
            UserName: "naeem@example.com",
            SecurityStamp: $"stamp-{userId:N}");
}

[Trait("Category", "AuthCQRS")]
public sealed class LogoutCommandHandlerTests
{
    [Fact(
        DisplayName =
            "Logout invalidates security-stamp cache even when the identity cannot be resolved")]
    public async Task
        MissingIdentity_InvalidatesSecurityStampCache_WithoutUpdatingIdentityStamp()
    {
        var userId =
            Guid.NewGuid();

        var refreshTokens =
            new Mock<IRefreshTokenRepository>();

        refreshTokens
            .Setup(x => x.RevokeUserTokenAsync(
                userId,
                "refresh-token",
                It.IsAny<DateTime>(),
                "198.51.100.20",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var identityService =
            new Mock<ILogoutIdentityService>();

        identityService
            .Setup(x => x.FindByIdAsync(
                userId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdentityAccountSnapshot?)null);

        var cacheInvalidator =
            new Mock<IUserSecurityStampCacheInvalidator>();

        cacheInvalidator
            .Setup(x => x.InvalidateAsync(
                userId,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler =
            new LogoutCommandHandler(
                refreshTokens.Object,
                identityService.Object,
                cacheInvalidator.Object,
                NullLogger<LogoutCommandHandler>.Instance);

        await handler.Handle(
            new LogoutCommand(
                userId,
                "refresh-token",
                "198.51.100.20"),
            CancellationToken.None);

        refreshTokens.Verify(x => x.RevokeUserTokenAsync(
            userId,
            "refresh-token",
            It.IsAny<DateTime>(),
            "198.51.100.20",
            It.IsAny<CancellationToken>()),
            Times.Once);

        identityService.Verify(x => x.UpdateSecurityStampAsync(
            It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()),
            Times.Never);

        cacheInvalidator.Verify(x => x.InvalidateAsync(
            userId,
            It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
