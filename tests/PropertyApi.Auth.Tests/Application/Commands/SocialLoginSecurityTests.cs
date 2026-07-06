using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;
using PropertyApi.Application.Auth.Commands.SocialLogin;
using PropertyApi.Application.Auth.Contracts;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Application.Auth.Models;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Domain.Users.Entities;
using Xunit;

namespace PropertyApi.Auth.Tests.Application.Commands;

public sealed class SocialLoginSecurityTests
{
    private const string Provider = "Google";
    private const string RawToken = "valid-google-id-token";
    private const string ProviderId = "google-sub-123";
    private const string ExternalEmail = "victim@example.com";
    private const string IpAddress = "203.0.113.10";

    private readonly Mock<ISocialTokenVerifier> _verifier = new(MockBehavior.Strict);
    private readonly Mock<IIdentityUserService> _identityUsers = new(MockBehavior.Loose);
    private readonly Mock<ITokenService> _tokenService = new(MockBehavior.Loose);
    private readonly Mock<IRefreshTokenRepository> _refreshTokens = new(MockBehavior.Loose);
    private readonly Mock<IJwtTokenSettings> _jwtSettings = new(MockBehavior.Loose);
    private readonly Mock<IUnitOfWork> _unitOfWork = new(MockBehavior.Loose);
    private readonly Mock<IAuditLogService> _auditLogs = new(MockBehavior.Loose);
    private readonly Mock<ILogger<SocialLoginCommandHandler>> _logger = new(MockBehavior.Loose);

    public SocialLoginSecurityTests()
    {
        _verifier.SetupGet(x => x.ProviderName).Returns(Provider);
        _jwtSettings.SetupGet(x => x.AccessTokenMinutes).Returns(15);

        // Keep success-path tests independent from the exact role collection type
        // accepted by GenerateAccessToken(...).
        _tokenService.SetReturnsDefault<string>("generated-token");
    }

    [Fact]
    public async Task Handle_ProviderIdAlreadyLinked_AndTokenHasNoEmail_SignsInSuccessfully()
    {
        // Arrange
        var socialUser = SocialUser(
            email: null,
            isEmailVerified: false);

        var linkedUser = LocalUser(
            email: "linked@example.com",
            emailConfirmed: true);

        SetupVerifiedSocialUser(socialUser);

        _identityUsers
            .Setup(x => x.FindByLoginAsync(
                Provider,
                ProviderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(linkedUser);

        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Command(), CancellationToken.None);

        // Assert
        AssertSucceeded(result);

        AssertMethodCalled(_tokenService, nameof(ITokenService.GenerateAccessToken));
        AssertMethodCalled(_tokenService, nameof(ITokenService.GenerateRefreshToken));
        AssertMethodCalled(_refreshTokens, nameof(IRefreshTokenRepository.AddAsync));

        AssertMethodNotCalled(_identityUsers, nameof(IIdentityUserService.AddLoginAsync));
        AssertMethodNotCalled(_identityUsers, nameof(IIdentityUserService.CreateAsync));
        AssertMethodNotCalled(_identityUsers, nameof(IIdentityUserService.FindByEmailAsync));
    }

    [Fact]
    public async Task Handle_UnverifiedExternalEmail_MatchingExistingUser_IsRejectedWithoutSideEffects()
    {
        // Arrange
        var socialUser = SocialUser(
            email: ExternalEmail,
            isEmailVerified: false);

        var existingUser = LocalUser(
            email: ExternalEmail,
            emailConfirmed: true);

        SetupVerifiedSocialUser(socialUser);

        _identityUsers
            .Setup(x => x.FindByLoginAsync(
                Provider,
                ProviderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // The handler must reject before trusting the external email enough to query/link by it.
        _identityUsers
            .Setup(x => x.FindByEmailAsync(
                ExternalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Command(), CancellationToken.None);

        // Assert
        AssertFailed(result);
        AssertRejectedWithoutSecuritySideEffects();
        AssertMethodNotCalled(_identityUsers, nameof(IIdentityUserService.FindByEmailAsync));
    }

    [Fact]
    public async Task Handle_UnverifiedExternalEmail_WithNoExistingUser_IsRejectedAndDoesNotCreateAccount()
    {
        // Arrange
        var socialUser = SocialUser(
            email: ExternalEmail,
            isEmailVerified: false);

        SetupVerifiedSocialUser(socialUser);

        _identityUsers
            .Setup(x => x.FindByLoginAsync(
                Provider,
                ProviderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _identityUsers
            .Setup(x => x.FindByEmailAsync(
                ExternalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Command(), CancellationToken.None);

        // Assert
        AssertFailed(result);
        AssertRejectedWithoutSecuritySideEffects();
        AssertMethodNotCalled(_identityUsers, nameof(IIdentityUserService.FindByEmailAsync));
    }

    [Fact]
    public async Task Handle_VerifiedExternalEmail_AndConfirmedLocalAccount_LinksAndSignsIn()
    {
        // Arrange
        var socialUser = SocialUser(
            email: ExternalEmail,
            isEmailVerified: true);

        var existingUser = LocalUser(
            email: ExternalEmail,
            emailConfirmed: true);

        SetupVerifiedSocialUser(socialUser);

        _identityUsers
            .Setup(x => x.FindByLoginAsync(
                Provider,
                ProviderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _identityUsers
            .Setup(x => x.FindByEmailAsync(
                ExternalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        _identityUsers
            .Setup(x => x.AddLoginAsync(
                existingUser,
                Provider,
                ProviderId,
                Provider,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityOperationResult.Success);

        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Command(), CancellationToken.None);

        // Assert
        AssertSucceeded(result);

        _identityUsers.Verify(x => x.AddLoginAsync(
            existingUser,
            Provider,
            ProviderId,
            Provider,
            It.IsAny<CancellationToken>()), Times.Once);

        AssertMethodNotCalled(_identityUsers, nameof(IIdentityUserService.CreateAsync));
        AssertMethodCalled(_tokenService, nameof(ITokenService.GenerateAccessToken));
        AssertMethodCalled(_tokenService, nameof(ITokenService.GenerateRefreshToken));
        AssertMethodCalled(_refreshTokens, nameof(IRefreshTokenRepository.AddAsync));
    }

    [Fact]
    public async Task Handle_VerifiedExternalEmail_AndUnconfirmedLocalAccount_IsRejectedWithoutLinkOrTokens()
    {
        // Arrange
        var socialUser = SocialUser(
            email: ExternalEmail,
            isEmailVerified: true);

        var existingUser = LocalUser(
            email: ExternalEmail,
            emailConfirmed: false);

        SetupVerifiedSocialUser(socialUser);

        _identityUsers
            .Setup(x => x.FindByLoginAsync(
                Provider,
                ProviderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _identityUsers
            .Setup(x => x.FindByEmailAsync(
                ExternalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Command(), CancellationToken.None);

        // Assert
        AssertFailed(result);
        AssertRejectedWithoutSecuritySideEffects();
    }

    [Fact]
    public async Task Handle_DeletedAccount_IsRejectedAndNeverLinked()
    {
        // Arrange
        var socialUser = SocialUser(
            email: ExternalEmail,
            isEmailVerified: true);

        var deletedUser = LocalUser(
            email: ExternalEmail,
            emailConfirmed: true,
            isDeleted: true);

        SetupVerifiedSocialUser(socialUser);

        _identityUsers
            .Setup(x => x.FindByLoginAsync(
                Provider,
                ProviderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _identityUsers
            .Setup(x => x.FindByEmailAsync(
                ExternalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(deletedUser);

        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Command(), CancellationToken.None);

        // Assert
        AssertFailed(result);
        AssertRejectedWithoutSecuritySideEffects();
    }

    [Fact]
    public async Task Handle_AddLoginAsyncFails_DoesNotIssueAccessOrRefreshToken()
    {
        // Arrange
        var socialUser = SocialUser(
            email: ExternalEmail,
            isEmailVerified: true);

        var existingUser = LocalUser(
            email: ExternalEmail,
            emailConfirmed: true);

        SetupVerifiedSocialUser(socialUser);

        _identityUsers
            .Setup(x => x.FindByLoginAsync(
                Provider,
                ProviderId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        _identityUsers
            .Setup(x => x.FindByEmailAsync(
                ExternalEmail,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        _identityUsers
            .Setup(x => x.AddLoginAsync(
                existingUser,
                Provider,
                ProviderId,
                Provider,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(
    new IdentityOperationResult(
        false,
        new[]
        {
            "LoginAlreadyAssociated: The external login could not be linked."
        }));

        var sut = CreateSut();

        // Act
        var result = await sut.Handle(Command(), CancellationToken.None);

        // Assert
        AssertFailed(result);

        _identityUsers.Verify(x => x.AddLoginAsync(
            existingUser,
            Provider,
            ProviderId,
            Provider,
            It.IsAny<CancellationToken>()), Times.Once);

        AssertMethodNotCalled(_identityUsers, nameof(IIdentityUserService.CreateAsync));
        AssertNoTokenIssuance();
    }

    private SocialLoginCommandHandler CreateSut()
        => new(
            new[] { _verifier.Object },
            _identityUsers.Object,
            _tokenService.Object,
            _refreshTokens.Object,
            _jwtSettings.Object,
            _auditLogs.Object,
            _unitOfWork.Object,
            _logger.Object);


    private void SetupVerifiedSocialUser(SocialUserInfo socialUser)
    {
        _verifier
            .Setup(x => x.VerifyAsync(
                RawToken,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(socialUser);
    }

    private void AssertRejectedWithoutSecuritySideEffects()
    {
        AssertMethodNotCalled(_identityUsers, nameof(IIdentityUserService.AddLoginAsync));
        AssertMethodNotCalled(_identityUsers, nameof(IIdentityUserService.CreateAsync));
        AssertNoTokenIssuance();
    }

    private void AssertNoTokenIssuance()
    {
        AssertMethodNotCalled(_tokenService, nameof(ITokenService.GenerateAccessToken));
        AssertMethodNotCalled(_tokenService, nameof(ITokenService.GenerateRefreshToken));
        AssertMethodNotCalled(_refreshTokens, nameof(IRefreshTokenRepository.AddAsync));
    }

    private static void AssertMethodCalled<T>(Mock<T> mock, string methodName)
        where T : class
    {
        Assert.Contains(
            mock.Invocations,
            invocation => invocation.Method.Name == methodName);
    }

    private static void AssertMethodNotCalled<T>(Mock<T> mock, string methodName)
        where T : class
    {
        Assert.DoesNotContain(
            mock.Invocations,
            invocation => invocation.Method.Name == methodName);
    }

    private static void AssertSucceeded(SocialLoginResult result)
        => Assert.True(ReadSuccessFlag(result), "Expected social login to succeed.");

    private static void AssertFailed(SocialLoginResult result)
        => Assert.False(ReadSuccessFlag(result), "Expected social login to be rejected.");

    private static bool ReadSuccessFlag(SocialLoginResult result)
    {
        var type = result.GetType();
        var candidateNames = new[] { "Succeeded", "Success", "IsSuccess", "IsSuccessful" };

        foreach (var candidateName in candidateNames)
        {
            var property = type.GetProperty(
                candidateName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

            if (property?.PropertyType == typeof(bool))
            {
                return (bool)property.GetValue(result)!;
            }
        }

        throw new InvalidOperationException(
            $"Could not find a boolean success flag on {type.FullName}. " +
            "Update ReadSuccessFlag(...) to match SocialLoginResult.");
    }

    private static SocialLoginCommand Command()
        => CreateUsingNamedValues<SocialLoginCommand>(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["GoogleIdToken"] = RawToken,
            ["AppleIdentityToken"] = null,
            ["AppleFirstName"] = null,
            ["AppleLastName"] = null,
            ["IpAddress"] = IpAddress
        });

    private static SocialUserInfo SocialUser(
        string? email,
        bool isEmailVerified)
        => CreateUsingNamedValues<SocialUserInfo>(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ProviderName"] = Provider,
            ["ProviderId"] = ProviderId,
            ["Email"] = email,
            ["IsEmailVerified"] = isEmailVerified,
            ["FirstName"] = "Security",
            ["LastName"] = "Test",
            ["AvatarUrl"] = null
        });

    private static User LocalUser(
        string email,
        bool emailConfirmed,
        bool isDeleted = false)
        => new()
        {
            UserName = email,
            Email = email,
            EmailConfirmed = emailConfirmed,
            FirstName = "Local",
            LastName = "User",
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    private static T CreateUsingNamedValues<T>(
        IReadOnlyDictionary<string, object?> values)
    {
        var type = typeof(T);

        var constructors = type
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderByDescending(x => x.GetParameters().Length);

        foreach (var constructor in constructors)
        {
            var parameters = constructor.GetParameters();
            var arguments = new object?[parameters.Length];
            var canUseConstructor = true;

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];

                if (parameter.Name is not null &&
                    values.TryGetValue(parameter.Name, out var suppliedValue))
                {
                    arguments[i] = suppliedValue;
                    continue;
                }

                if (parameter.HasDefaultValue)
                {
                    arguments[i] = parameter.DefaultValue;
                    continue;
                }

                if (!parameter.ParameterType.IsValueType ||
                    Nullable.GetUnderlyingType(parameter.ParameterType) is not null)
                {
                    arguments[i] = null;
                    continue;
                }

                canUseConstructor = false;
                break;
            }

            if (!canUseConstructor)
            {
                continue;
            }

            var instance = (T)constructor.Invoke(arguments);
            ApplyWritableProperties(instance, values);
            return instance;
        }

        throw new InvalidOperationException(
            $"Could not construct {type.FullName} from the supplied named values.");
    }

    private static void ApplyWritableProperties<T>(
        T instance,
        IReadOnlyDictionary<string, object?> values)
    {
        var type = instance!.GetType();

        foreach (var pair in values)
        {
            var property = type.GetProperty(
                pair.Key,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

            if (property?.CanWrite == true)
            {
                property.SetValue(instance, pair.Value);
            }
        }
    }
}
