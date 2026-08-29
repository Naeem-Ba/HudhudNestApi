using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using PropertyApi.Application.Auth.Abstractions;
using PropertyApi.Application.Auth.Commands.SocialLogin;
using PropertyApi.Application.Auth.Contracts;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Auth.Policies;
using PropertyApi.Application.Auth.Services;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Infrastructure.Identity.Services;

namespace PropertyApi.Auth.Tests.Application;

public sealed class AuthenticationDecompositionTests
{
    [Fact]
    public void SocialHandler_DependsOnlyOnOrchestrator()
    {
        var parameter = Assert.Single(
            Assert.Single(typeof(SocialLoginCommandHandler).GetConstructors()).GetParameters());
        Assert.Equal(typeof(ISocialAuthenticationOrchestrator), parameter.ParameterType);
    }

    [Fact]
    public void CompatibilityFacade_DoesNotOwnUserManager()
    {
#pragma warning disable CS0618
        var fields = typeof(PureIdentityService).GetFields(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);
#pragma warning restore CS0618
        Assert.DoesNotContain(fields, field =>
            field.FieldType.IsGenericType &&
            field.FieldType.GetGenericTypeDefinition() == typeof(UserManager<>));
    }

    [Fact]
    public void LinkingPolicy_RequiresVerifiedProviderAndConfirmedLocalEmail()
    {
        var policy = new SocialAccountLinkingPolicy();
        Assert.Equal(
            AccountLinkingDecision.Allowed,
            policy.Evaluate(SocialUser(emailVerified: true), Account(emailConfirmed: true)));
        Assert.Equal(
            AccountLinkingDecision.Forbidden,
            policy.Evaluate(SocialUser(emailVerified: false), Account(emailConfirmed: true)));
        Assert.Equal(
            AccountLinkingDecision.AdditionalVerificationRequired,
            policy.Evaluate(SocialUser(emailVerified: true), Account(emailConfirmed: false)));
    }

    [Fact]
    public void CreationPolicy_RejectsUnverifiedClaimedEmail()
    {
        var policy = new SocialAccountCreationPolicy();
        Assert.Equal(AccountCreationDecision.Forbidden, policy.Evaluate(SocialUser(false)));
        Assert.Equal(AccountCreationDecision.Allowed, policy.Evaluate(SocialUser(true)));
    }

    [Fact]
    public void PhoneOwnershipPolicy_RejectsDeletedIdentity()
    {
        var policy = new PhoneOwnershipPolicy();
        Assert.Equal(PhoneOwnershipDecision.Available, policy.Evaluate(null));
        Assert.Equal(
            PhoneOwnershipDecision.Forbidden,
            policy.Evaluate(Account(emailConfirmed: true, deleted: true)));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SessionIssuer_RejectsBannedOrLockedIdentity(
        bool banned,
        bool locked)
    {
        var tokens = new Mock<ITokenService>(MockBehavior.Strict);
        var issuer = new AuthenticationSessionIssuer(
            Mock.Of<ILoginIdentityService>(),
            tokens.Object,
            Mock.Of<IRefreshTokenRepository>(),
            Mock.Of<IJwtTokenSettings>(),
            Mock.Of<IAuditLogService>(),
            NullLogger<AuthenticationSessionIssuer>.Instance);
        var identity = Account(emailConfirmed: true) with
        {
            IsBanned = banned,
            LockoutEndUtc = locked ? DateTimeOffset.UtcNow.AddMinutes(5) : null
        };

        var result = await issuer.IssueAsync(
            new AuthenticationSessionRequest(
                identity,
                "test",
                null,
                SuccessfulLoginRecordingMode.None),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("ACCOUNT_UNAVAILABLE", result.ErrorCode);
        tokens.VerifyNoOtherCalls();
    }

    private static SocialUserInfo SocialUser(bool emailVerified) =>
        new("provider-subject", "Google", "verified@example.test", emailVerified, null, null, null);

    private static IdentityAccountSnapshot Account(bool emailConfirmed, bool deleted = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "local@example.test", null,
            emailConfirmed, false, true, deleted);
}
