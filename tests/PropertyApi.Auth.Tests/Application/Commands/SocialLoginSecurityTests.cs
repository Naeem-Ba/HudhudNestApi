using System.Reflection;
using Microsoft.Extensions.Logging;
using Moq;
using PropertyApi.Application.Auth.Commands.SocialLogin;
using PropertyApi.Application.Auth.Contracts;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Users.Interfaces;
using Xunit;

namespace PropertyApi.Auth.Tests.Application.Commands;

public sealed class SocialLoginSecurityTests
{
    private const string Provider = "Google";
    private const string RawToken = "valid-google-id-token";
    private const string ProviderId = "google-sub-123";
    private const string ExternalEmail = "victim@example.com";
    private const string IpAddress = "203.0.113.10";

    private readonly Mock<ISocialTokenVerifier> _verifier =
        new(MockBehavior.Strict);

    private readonly Mock<IPureIdentityService> _identity =
        new(MockBehavior.Loose);

    private readonly Mock<ITokenService> _tokenService =
        new(MockBehavior.Loose);

    private readonly Mock<IRefreshTokenRepository> _refreshTokens =
        new(MockBehavior.Loose);

    private readonly Mock<IJwtTokenSettings> _jwtSettings =
        new(MockBehavior.Loose);

    private readonly Mock<IUnitOfWork> _unitOfWork =
        new(MockBehavior.Loose);

    private readonly Mock<IAuditLogService> _auditLogs =
        new(MockBehavior.Loose);

    private readonly Mock<ILogger<SocialLoginCommandHandler>> _logger =
        new(MockBehavior.Loose);

    public SocialLoginSecurityTests()
    {
        _verifier
            .SetupGet(x => x.ProviderName)
            .Returns(Provider);

        _jwtSettings
            .SetupGet(x => x.AccessTokenMinutes)
            .Returns(15);

        /*
         * Keep success-path tests independent from the exact token
         * implementation while still exercising the handler flow.
         */
        _tokenService.SetReturnsDefault<string>(
            "generated-token");

        _identity
            .Setup(x => x.RecordSuccessfulLoginAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        _identity
            .Setup(x => x.GetRolesAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                Array.Empty<string>());
    }

    [Fact]
    public async Task
        Handle_ProviderIdAlreadyLinked_AndTokenHasNoEmail_SignsInSuccessfully()
    {
        // Arrange
        var socialUser =
            SocialUser(
                email: null,
                isEmailVerified: false);

        var linkedIdentity =
            LocalIdentity(
                email: "linked@example.com",
                emailConfirmed: true);

        SetupVerifiedSocialUser(
            socialUser);

        _identity
            .Setup(x => x.FindByLoginAsync(
                Provider,
                ProviderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                linkedIdentity);

        var sut =
            CreateSut();

        // Act
        var result =
            await sut.Handle(
                Command(),
                CancellationToken.None);

        // Assert
        AssertSucceeded(
            result);

        AssertMethodCalled(
            _tokenService,
            nameof(ITokenService.GenerateAccessToken));

        AssertMethodCalled(
            _tokenService,
            nameof(ITokenService.GenerateRefreshToken));

        AssertMethodCalled(
            _refreshTokens,
            nameof(IRefreshTokenRepository.AddAsync));

        AssertMethodNotCalled(
            _identity,
            nameof(IPureIdentityService.AddLoginAsync));

        AssertMethodNotCalled(
            _identity,
            nameof(IPureIdentityService.CreateAsync));

        AssertMethodNotCalled(
            _identity,
            nameof(IPureIdentityService.FindByEmailAsync));
    }

    [Fact]
    public async Task
        Handle_UnverifiedExternalEmail_MatchingExistingUser_IsRejectedWithoutSideEffects()
    {
        // Arrange
        var socialUser =
            SocialUser(
                email: ExternalEmail,
                isEmailVerified: false);

        var existingIdentity =
            LocalIdentity(
                email: ExternalEmail,
                emailConfirmed: true);

        SetupVerifiedSocialUser(
            socialUser);

        _identity
            .Setup(x => x.FindByLoginAsync(
                Provider,
                ProviderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (IdentityAccountSnapshot?)null);

        /*
         * The handler must reject before trusting the external email
         * enough to query or link a local account.
         *
         * This setup intentionally exists to prove that the method
         * is not invoked.
         */
        _identity
            .Setup(x => x.FindByEmailAsync(
                ExternalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                existingIdentity);

        var sut =
            CreateSut();

        // Act
        var result =
            await sut.Handle(
                Command(),
                CancellationToken.None);

        // Assert
        AssertFailed(
            result);

        AssertRejectedWithoutSecuritySideEffects();

        AssertMethodNotCalled(
            _identity,
            nameof(IPureIdentityService.FindByEmailAsync));
    }

    [Fact]
    public async Task
        Handle_UnverifiedExternalEmail_WithNoExistingUser_IsRejectedAndDoesNotCreateAccount()
    {
        // Arrange
        var socialUser =
            SocialUser(
                email: ExternalEmail,
                isEmailVerified: false);

        SetupVerifiedSocialUser(
            socialUser);

        _identity
            .Setup(x => x.FindByLoginAsync(
                Provider,
                ProviderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (IdentityAccountSnapshot?)null);

        _identity
            .Setup(x => x.FindByEmailAsync(
                ExternalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (IdentityAccountSnapshot?)null);

        var sut =
            CreateSut();

        // Act
        var result =
            await sut.Handle(
                Command(),
                CancellationToken.None);

        // Assert
        AssertFailed(
            result);

        AssertRejectedWithoutSecuritySideEffects();

        AssertMethodNotCalled(
            _identity,
            nameof(IPureIdentityService.FindByEmailAsync));
    }

    [Fact]
    public async Task
        Handle_VerifiedExternalEmail_AndConfirmedLocalAccount_LinksAndSignsIn()
    {
        // Arrange
        var socialUser =
            SocialUser(
                email: ExternalEmail,
                isEmailVerified: true);

        var existingIdentity =
            LocalIdentity(
                email: ExternalEmail,
                emailConfirmed: true);

        SetupVerifiedSocialUser(
            socialUser);

        _identity
            .Setup(x => x.FindByLoginAsync(
                Provider,
                ProviderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (IdentityAccountSnapshot?)null);

        _identity
            .Setup(x => x.FindByEmailAsync(
                ExternalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                existingIdentity);

        _identity
            .Setup(x => x.AddLoginAsync(
                existingIdentity.IdentityId,
                Provider,
                ProviderId,
                Provider,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                IdentityOperationResult.Success());

        var sut =
            CreateSut();

        // Act
        var result =
            await sut.Handle(
                Command(),
                CancellationToken.None);

        // Assert
        AssertSucceeded(
            result);

        _identity.Verify(
            x => x.AddLoginAsync(
                existingIdentity.IdentityId,
                Provider,
                ProviderId,
                Provider,
                It.IsAny<CancellationToken>()),
            Times.Once);

        AssertMethodNotCalled(
            _identity,
            nameof(IPureIdentityService.CreateAsync));

        AssertMethodCalled(
            _tokenService,
            nameof(ITokenService.GenerateAccessToken));

        AssertMethodCalled(
            _tokenService,
            nameof(ITokenService.GenerateRefreshToken));

        AssertMethodCalled(
            _refreshTokens,
            nameof(IRefreshTokenRepository.AddAsync));
    }

    [Fact]
    public async Task
        Handle_VerifiedExternalEmail_AndUnconfirmedLocalAccount_IsRejectedWithoutLinkOrTokens()
    {
        // Arrange
        var socialUser =
            SocialUser(
                email: ExternalEmail,
                isEmailVerified: true);

        var existingIdentity =
            LocalIdentity(
                email: ExternalEmail,
                emailConfirmed: false);

        SetupVerifiedSocialUser(
            socialUser);

        _identity
            .Setup(x => x.FindByLoginAsync(
                Provider,
                ProviderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (IdentityAccountSnapshot?)null);

        _identity
            .Setup(x => x.FindByEmailAsync(
                ExternalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                existingIdentity);

        var sut =
            CreateSut();

        // Act
        var result =
            await sut.Handle(
                Command(),
                CancellationToken.None);

        // Assert
        AssertFailed(
            result);

        AssertRejectedWithoutSecuritySideEffects();
    }

    [Fact]
    public async Task
        Handle_DeletedAccount_IsRejectedAndNeverLinked()
    {
        // Arrange
        var socialUser =
            SocialUser(
                email: ExternalEmail,
                isEmailVerified: true);

        var deletedIdentity =
            LocalIdentity(
                email: ExternalEmail,
                emailConfirmed: true,
                isDeleted: true);

        SetupVerifiedSocialUser(
            socialUser);

        _identity
            .Setup(x => x.FindByLoginAsync(
                Provider,
                ProviderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (IdentityAccountSnapshot?)null);

        _identity
            .Setup(x => x.FindByEmailAsync(
                ExternalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                deletedIdentity);

        var sut =
            CreateSut();

        // Act
        var result =
            await sut.Handle(
                Command(),
                CancellationToken.None);

        // Assert
        AssertFailed(
            result);

        AssertRejectedWithoutSecuritySideEffects();
    }

    [Fact]
    public async Task
        Handle_AddLoginAsyncFails_DoesNotIssueAccessOrRefreshToken()
    {
        // Arrange
        var socialUser =
            SocialUser(
                email: ExternalEmail,
                isEmailVerified: true);

        var existingIdentity =
            LocalIdentity(
                email: ExternalEmail,
                emailConfirmed: true);

        SetupVerifiedSocialUser(
            socialUser);

        _identity
            .Setup(x => x.FindByLoginAsync(
                Provider,
                ProviderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                (IdentityAccountSnapshot?)null);

        _identity
            .Setup(x => x.FindByEmailAsync(
                ExternalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                existingIdentity);

        _identity
            .Setup(x => x.AddLoginAsync(
                existingIdentity.IdentityId,
                Provider,
                ProviderId,
                Provider,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new IdentityOperationResult(
                    false,
                    new[]
                    {
                        "LoginAlreadyAssociated: " +
                        "The external login could not be linked."
                    }));

        var sut =
            CreateSut();

        // Act
        var result =
            await sut.Handle(
                Command(),
                CancellationToken.None);

        // Assert
        AssertFailed(
            result);

        _identity.Verify(
            x => x.AddLoginAsync(
                existingIdentity.IdentityId,
                Provider,
                ProviderId,
                Provider,
                It.IsAny<CancellationToken>()),
            Times.Once);

        AssertMethodNotCalled(
            _identity,
            nameof(IPureIdentityService.CreateAsync));

        AssertNoTokenIssuance();
    }

    private SocialLoginCommandHandler CreateSut()
        => new(
            new[]
            {
                _verifier.Object
            },
            _identity.Object,
            Mock.Of<IUserAccountRepository>(),
            _tokenService.Object,
            _refreshTokens.Object,
            _jwtSettings.Object,
            _auditLogs.Object,
            _unitOfWork.Object,
            _logger.Object);

    private void SetupVerifiedSocialUser(
        SocialUserInfo socialUser)
    {
        _verifier
            .Setup(x => x.VerifyAsync(
                RawToken,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                socialUser);
    }

    private void
        AssertRejectedWithoutSecuritySideEffects()
    {
        AssertMethodNotCalled(
            _identity,
            nameof(IPureIdentityService.AddLoginAsync));

        AssertMethodNotCalled(
            _identity,
            nameof(IPureIdentityService.CreateAsync));

        AssertNoTokenIssuance();
    }

    private void AssertNoTokenIssuance()
    {
        AssertMethodNotCalled(
            _tokenService,
            nameof(ITokenService.GenerateAccessToken));

        AssertMethodNotCalled(
            _tokenService,
            nameof(ITokenService.GenerateRefreshToken));

        AssertMethodNotCalled(
            _refreshTokens,
            nameof(IRefreshTokenRepository.AddAsync));
    }

    private static void AssertMethodCalled<T>(
        Mock<T> mock,
        string methodName)
        where T : class
    {
        Assert.Contains(
            mock.Invocations,
            invocation =>
                invocation.Method.Name ==
                methodName);
    }

    private static void AssertMethodNotCalled<T>(
        Mock<T> mock,
        string methodName)
        where T : class
    {
        Assert.DoesNotContain(
            mock.Invocations,
            invocation =>
                invocation.Method.Name ==
                methodName);
    }

    private static void AssertSucceeded(
        SocialLoginResult result)
        => Assert.True(
            ReadSuccessFlag(result),
            "Expected social login to succeed.");

    private static void AssertFailed(
        SocialLoginResult result)
        => Assert.False(
            ReadSuccessFlag(result),
            "Expected social login to be rejected.");

    private static bool ReadSuccessFlag(
        SocialLoginResult result)
    {
        var type =
            result.GetType();

        var candidateNames =
            new[]
            {
                "Succeeded",
                "Success",
                "IsSuccess",
                "IsSuccessful"
            };

        foreach (var candidateName in candidateNames)
        {
            var property =
                type.GetProperty(
                    candidateName,
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.IgnoreCase);

            if (property?.PropertyType == typeof(bool))
            {
                return (bool)property.GetValue(
                    result)!;
            }
        }

        throw new InvalidOperationException(
            $"Could not find a boolean success flag on {type.FullName}. " +
            "Update ReadSuccessFlag(...) to match SocialLoginResult.");
    }

    private static SocialLoginCommand Command()
        => CreateUsingNamedValues<SocialLoginCommand>(
            new Dictionary<string, object?>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["GoogleIdToken"] =
                    RawToken,

                ["AppleIdentityToken"] =
                    null,

                ["AppleFirstName"] =
                    null,

                ["AppleLastName"] =
                    null,

                ["IpAddress"] =
                    IpAddress
            });

    private static SocialUserInfo SocialUser(
        string? email,
        bool isEmailVerified)
        => CreateUsingNamedValues<SocialUserInfo>(
            new Dictionary<string, object?>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["ProviderName"] =
                    Provider,

                ["ProviderId"] =
                    ProviderId,

                ["Email"] =
                    email,

                ["IsEmailVerified"] =
                    isEmailVerified,

                ["FirstName"] =
                    "Security",

                ["LastName"] =
                    "Test",

                ["AvatarUrl"] =
                    null
            });

    private static IdentityAccountSnapshot LocalIdentity(
        string email,
        bool emailConfirmed,
        bool isDeleted = false)
    {
        var identityId =
            Guid.NewGuid();

        return new IdentityAccountSnapshot(
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
                true,

            IsDeleted:
                isDeleted,

            UserName:
                email,

            SecurityStamp:
                $"security-stamp-{identityId:N}");
    }

    private static T CreateUsingNamedValues<T>(
        IReadOnlyDictionary<string, object?> values)
    {
        var type =
            typeof(T);

        var constructors =
            type
                .GetConstructors(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic)
                .OrderByDescending(
                    constructor =>
                        constructor
                            .GetParameters()
                            .Length);

        foreach (var constructor in constructors)
        {
            var parameters =
                constructor.GetParameters();

            var arguments =
                new object?[parameters.Length];

            var canUseConstructor =
                true;

            for (var index = 0;
                 index < parameters.Length;
                 index++)
            {
                var parameter =
                    parameters[index];

                if (parameter.Name is not null &&
                    values.TryGetValue(
                        parameter.Name,
                        out var suppliedValue))
                {
                    arguments[index] =
                        suppliedValue;

                    continue;
                }

                if (parameter.HasDefaultValue)
                {
                    arguments[index] =
                        parameter.DefaultValue;

                    continue;
                }

                if (!parameter.ParameterType.IsValueType ||
                    Nullable.GetUnderlyingType(
                        parameter.ParameterType)
                    is not null)
                {
                    arguments[index] =
                        null;

                    continue;
                }

                canUseConstructor =
                    false;

                break;
            }

            if (!canUseConstructor)
            {
                continue;
            }

            var instance =
                (T)constructor.Invoke(
                    arguments);

            ApplyWritableProperties(
                instance,
                values);

            return instance;
        }

        throw new InvalidOperationException(
            $"Could not construct {type.FullName} " +
            "from the supplied named values.");
    }

    private static void ApplyWritableProperties<T>(
        T instance,
        IReadOnlyDictionary<string, object?> values)
    {
        var type =
            instance!.GetType();

        foreach (var pair in values)
        {
            var property =
                type.GetProperty(
                    pair.Key,
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.IgnoreCase);

            if (property?.CanWrite == true)
            {
                property.SetValue(
                    instance,
                    pair.Value);
            }
        }
    }
}