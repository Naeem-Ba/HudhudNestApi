using Microsoft.Extensions.Logging.Abstractions;
using HudhudNestApi.Application.Auth.Commands.RefreshToken;
using HudhudNestApi.Application.Auth.Models;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Domain.Users.Constants;

namespace HudhudNestApi.Auth.Tests.Application.Commands;

/// <summary>
/// "No automatic sign-out" -- the refresh-token cases that used to end a healthy session:
/// a refresh whose answer never reached the phone, two overlapping refreshes, a token ended on purpose
/// (sign-in on a further device, logout, password change) being presented later.
/// </summary>
[Trait("Category", "AuthCQRS")]
public sealed class RefreshTokenSessionContinuityTests
{
    private const string OldToken = "old-refresh-token";
    private const string NewToken = "new-refresh-token";
    private const string Ip = "203.0.113.10";

    private sealed class Fixture
    {
        public readonly Guid UserId = Guid.NewGuid();
        public readonly Mock<IRefreshTokenRepository> Tokens = new();
        public readonly Mock<IRefreshTokenIdentityService> Identity = new();
        public readonly Mock<ITokenService> TokenService = new();
        public readonly Mock<IUserSecurityStampCacheInvalidator> Cache = new();
        public readonly Mock<IAuditLogService> Audit = new();
        public readonly RefreshTokenCommandHandler Handler;

        public Fixture(RefreshTokenRecord stored, int graceSeconds, bool revokeIfActive = true)
        {
            Tokens.Setup(x => x.ExecuteInTransactionAsync(
                    It.IsAny<Func<CancellationToken, Task<RefreshTokenResult>>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((Func<CancellationToken, Task<RefreshTokenResult>> action, CancellationToken ct) => action(ct));
            Tokens.Setup(x => x.GetByRefreshTokenAsync(OldToken, It.IsAny<CancellationToken>())).ReturnsAsync(stored with { UserId = UserId });
            Tokens.Setup(x => x.RevokeIfActiveAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(revokeIfActive);

            Identity.Setup(x => x.FindByIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new IdentityAccountSnapshot(
                IdentityId: UserId, UserAccountId: UserId, Email: "a@example.com", PhoneNumber: null,
                EmailConfirmed: true, PhoneConfirmed: false, HasPassword: true, IsDeleted: false,
                UserName: "a@example.com", SecurityStamp: "stamp"));
            Identity.Setup(x => x.GetRolesAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { RoleNames.User });
            Identity.Setup(x => x.UpdateSecurityStampAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(IdentityOperationResult.Success());

            TokenService.Setup(x => x.GenerateRefreshToken()).Returns(NewToken);
            TokenService.Setup(x => x.GenerateAccessToken(It.IsAny<AccessTokenSubject>(), It.IsAny<IReadOnlyCollection<string>>()))
                .Returns("new-access-token");

            var settings = new Mock<IJwtTokenSettings>();
            settings.SetupGet(x => x.AccessTokenMinutes).Returns(30);
            settings.SetupGet(x => x.RefreshReuseGraceSeconds).Returns(graceSeconds);

            Handler = new RefreshTokenCommandHandler(
                Tokens.Object, Identity.Object, TokenService.Object, settings.Object,
                new RefreshTokenReuseHandler(Tokens.Object, Identity.Object, Cache.Object, Audit.Object, NullLogger<RefreshTokenReuseHandler>.Instance),
                NullLogger<RefreshTokenCommandHandler>.Instance);
        }

        public Task<RefreshTokenResult> RefreshAsync() =>
            Handler.Handle(new RefreshTokenCommand(OldToken, Ip), CancellationToken.None);

        public void VerifyNothingElseWasEnded()
        {
            Tokens.Verify(x => x.RevokeActiveTokensForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
            Identity.Verify(x => x.UpdateSecurityStampAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
            Cache.Verify(x => x.InvalidateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }

    private static RefreshTokenRecord Rotated(DateTime revokedAt) =>
        new(Guid.NewGuid(), Guid.Empty, DateTime.UtcNow.AddDays(20), IsRevoked: true, RevokedAt: revokedAt, ReplacedByTokenHash: "successor-hash");

    [Fact(DisplayName = "A rotated token presented again within the grace window gets a fresh session, not a sign-out")]
    public async Task LostResponse_RetriedWithinGrace_IssuesAFreshToken()
    {
        var f = new Fixture(Rotated(DateTime.UtcNow.AddSeconds(-20)), graceSeconds: 60);

        var result = await f.RefreshAsync();

        Assert.True(result.Success);
        Assert.Equal(NewToken, result.RefreshToken);
        Assert.Equal("new-access-token", result.AccessToken);
        f.Tokens.Verify(x => x.AddRotatedAsync(f.UserId, NewToken, Ip, It.IsAny<CancellationToken>()), Times.Once);
        f.Tokens.Verify(x => x.RevokeIfActiveAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        f.VerifyNothingElseWasEnded();
    }

    [Fact(DisplayName = "A rotated token presented after the grace window is still treated as theft")]
    public async Task ReplayOutsideGrace_StillRevokesEverything()
    {
        var f = new Fixture(Rotated(DateTime.UtcNow.AddSeconds(-61)), graceSeconds: 60);

        var result = await f.RefreshAsync();

        Assert.False(result.Success);
        Assert.Equal(RefreshTokenErrorCodes.ReuseDetected, result.ErrorCode);
        f.Tokens.Verify(x => x.RevokeActiveTokensForUserAsync(f.UserId, It.IsAny<DateTime>(), Ip, It.IsAny<CancellationToken>()), Times.Once);
        f.Tokens.Verify(x => x.AddRotatedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact(DisplayName = "With the grace window switched off a replay is theft immediately")]
    public async Task GraceOff_ReplayIsTheft()
    {
        var f = new Fixture(Rotated(DateTime.UtcNow.AddSeconds(-1)), graceSeconds: 0);

        var result = await f.RefreshAsync();

        Assert.Equal(RefreshTokenErrorCodes.ReuseDetected, result.ErrorCode);
    }

    [Fact(DisplayName = "A refresh that loses the race against another refresh of the same token still succeeds")]
    public async Task LostRace_GetsItsOwnToken()
    {
        var active = new RefreshTokenRecord(Guid.NewGuid(), Guid.Empty, DateTime.UtcNow.AddDays(20), IsRevoked: false);
        var f = new Fixture(active, graceSeconds: 60, revokeIfActive: false);

        var result = await f.RefreshAsync();

        Assert.True(result.Success);
        f.Tokens.Verify(x => x.AddRotatedAsync(f.UserId, NewToken, Ip, It.IsAny<CancellationToken>()), Times.Once);
        f.VerifyNothingElseWasEnded();
    }

    [Fact(DisplayName = "A session pushed out by a sign-in elsewhere is told so and revokes nothing else")]
    public async Task Superseded_IsNotTheft()
    {
        var stored = new RefreshTokenRecord(Guid.NewGuid(), Guid.Empty, DateTime.UtcNow.AddDays(20), IsRevoked: true,
            RevokedAt: DateTime.UtcNow.AddDays(-2), ReplacedByTokenHash: RefreshTokenMarkers.Superseded);
        var f = new Fixture(stored, graceSeconds: 60);

        var result = await f.RefreshAsync();

        Assert.False(result.Success);
        Assert.Equal(RefreshTokenErrorCodes.Superseded, result.ErrorCode);
        f.VerifyNothingElseWasEnded();
    }

    [Fact(DisplayName = "A token ended by logout or a password change is dead but is not read as theft")]
    public async Task EndedOnPurpose_IsNotTheft()
    {
        var stored = new RefreshTokenRecord(Guid.NewGuid(), Guid.Empty, DateTime.UtcNow.AddDays(20), IsRevoked: true,
            RevokedAt: DateTime.UtcNow.AddMinutes(-5), ReplacedByTokenHash: null);
        var f = new Fixture(stored, graceSeconds: 60);

        var result = await f.RefreshAsync();

        Assert.False(result.Success);
        Assert.Equal(RefreshTokenErrorCodes.Revoked, result.ErrorCode);
        f.VerifyNothingElseWasEnded();
    }

    [Fact(DisplayName = "A normal refresh rotates the token and ends no other device's session")]
    public async Task NormalRefresh_DoesNotEndOtherSessions()
    {
        var active = new RefreshTokenRecord(Guid.NewGuid(), Guid.Empty, DateTime.UtcNow.AddDays(20), IsRevoked: false);
        var f = new Fixture(active, graceSeconds: 60);

        var result = await f.RefreshAsync();

        Assert.True(result.Success);
        f.Tokens.Verify(x => x.AddRotatedAsync(f.UserId, NewToken, Ip, It.IsAny<CancellationToken>()), Times.Once);
        f.Tokens.Verify(x => x.AddAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        f.VerifyNothingElseWasEnded();
    }
}
