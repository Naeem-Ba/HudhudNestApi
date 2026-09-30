using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Auth.Commands.SocialLogin;
using HudhudNestApi.Infrastructure.Persistence;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Auth;

[Collection("AuthPostgres")]
public sealed class SocialLoginAtomicityPostgresTests
{
    private const string Provider = "Google";

    [Fact]
    public async Task CreateFailure_LeavesNoUserOrUserAccount()
    {
        var seed = Seed(
            "create-fail",
            UniqueEmail("social-create"));

        await using var factory = Factory(
            seed,
            new AuthFaultPlan
            {
                FailCreateAsync = true
            });

        await factory.PrepareDatabaseAsync();

        var result = await SendSocialAsync(
            factory,
            seed.RawToken);

        Assert.False(
            AuthTestReflection.IsSucceeded(result));

        Assert.Equal(
            0,
            await AuthDbAssertions.UserCountByEmailAsync(
                factory,
                seed.Email!));

        Assert.Equal(
            0,
            await AuthDbAssertions.LoginCountAsync(
                factory,
                Provider,
                seed.ProviderId));

        Assert.Equal(
            0,
            await UserAccountCountByEmailAsync(
                factory,
                seed.Email!));
    }

    [Fact]
    public async Task AddLoginFailure_RollsBackNewUserAndUserAccount()
    {
        var seed = Seed(
            "add-login-fail",
            UniqueEmail("social-login"));

        await using var factory = Factory(
            seed,
            new AuthFaultPlan
            {
                FailAddLoginAsync = true
            });

        await factory.PrepareDatabaseAsync();

        var result = await SendSocialAsync(
            factory,
            seed.RawToken);

        Assert.False(
            AuthTestReflection.IsSucceeded(result));

        Assert.Equal(
            0,
            await AuthDbAssertions.UserCountByEmailAsync(
                factory,
                seed.Email!));

        Assert.Equal(
            0,
            await AuthDbAssertions.LoginCountAsync(
                factory,
                Provider,
                seed.ProviderId));

        Assert.Equal(
            0,
            await UserAccountCountByEmailAsync(
                factory,
                seed.Email!));
    }

    [Fact]
    public async Task RoleFailure_RollsBackUserExternalLoginAndUserAccount()
    {
        var seed = Seed(
            "role-fail",
            UniqueEmail("social-role"));

        await using var factory = Factory(
            seed,
            new AuthFaultPlan
            {
                FailAddToRoleAsync = true
            });

        await factory.PrepareDatabaseAsync();

        var result = await SendSocialAsync(
            factory,
            seed.RawToken);

        Assert.False(
            AuthTestReflection.IsSucceeded(result));

        Assert.Equal(
            0,
            await AuthDbAssertions.UserCountByEmailAsync(
                factory,
                seed.Email!));

        Assert.Equal(
            0,
            await AuthDbAssertions.LoginCountAsync(
                factory,
                Provider,
                seed.ProviderId));

        Assert.Equal(
            0,
            await UserAccountCountByEmailAsync(
                factory,
                seed.Email!));
    }

    [Fact]
    public async Task RefreshTokenFailure_RollsBackNewUserExternalLoginAndUserAccount()
    {
        var seed = Seed(
            "refresh-fail",
            UniqueEmail("social-refresh"));

        await using var factory = Factory(
            seed,
            new AuthFaultPlan
            {
                FailRefreshTokenAddAsync = true
            });

        await factory.PrepareDatabaseAsync();

        await AssertFailureOrExceptionAsync(
            () => SendSocialAsync(
                factory,
                seed.RawToken));

        Assert.Equal(
            0,
            await AuthDbAssertions.UserCountByEmailAsync(
                factory,
                seed.Email!));

        Assert.Equal(
            0,
            await AuthDbAssertions.LoginCountAsync(
                factory,
                Provider,
                seed.ProviderId));

        Assert.Equal(
            0,
            await UserAccountCountByEmailAsync(
                factory,
                seed.Email!));
    }

    [Fact]
    public async Task ExistingAccount_LinkThenSignInFailure_DoesNotLeavePartialLogin()
    {
        var email =
            UniqueEmail("existing-link");

        var seed =
            Seed(
                "existing-link-token",
                email);

        await using var factory = Factory(
            seed,
            new AuthFaultPlan
            {
                FailRefreshTokenAddAsync = true
            });

        await factory.PrepareDatabaseAsync();

        await factory.SeedUserAsync(
            email,
            emailConfirmed: true);

        await AssertFailureOrExceptionAsync(
            () => SendSocialAsync(
                factory,
                seed.RawToken));

        Assert.Equal(
            1,
            await AuthDbAssertions.UserCountByEmailAsync(
                factory,
                email));

        Assert.Equal(
            0,
            await AuthDbAssertions.LoginCountAsync(
                factory,
                Provider,
                seed.ProviderId));
    }

    [Fact]
    public async Task Success_CreatesUserUserAccountLoginAndRole()
    {
        var email =
            UniqueEmail("social-success");

        var seed =
            Seed(
                "success",
                email);

        await using var factory =
            Factory(seed);

        await factory.PrepareDatabaseAsync();

        var result = await SendSocialAsync(
            factory,
            seed.RawToken);

        Assert.True(
            AuthTestReflection.IsSucceeded(result));

        Assert.Equal(
            1,
            await AuthDbAssertions.UserCountByEmailAsync(
                factory,
                email));

        Assert.Equal(
            1,
            await AuthDbAssertions.LoginCountAsync(
                factory,
                Provider,
                seed.ProviderId));

        Assert.Equal(
            1,
            await UserAccountCountByEmailAsync(
                factory,
                email));

        var userId = await UserIdByEmailAsync(
            factory,
            email);

        Assert.NotNull(userId);

        Assert.Equal(
            1,
            await AuthDbAssertions.RoleCountAsync(
                factory,
                userId!.Value));
    }

    [Fact]
    public async Task ConcurrentSameProviderAndProviderId_PersistsExactlyOneLoginLink()
    {
        var providerId =
            $"provider-{Guid.NewGuid():N}";

        var firstEmail =
            UniqueEmail("social-a");

        var secondEmail =
            UniqueEmail("social-b");

        var first = Seed(
            "token-a",
            firstEmail,
            providerId);

        var second = Seed(
            "token-b",
            secondEmail,
            providerId);

        await using var factory =
            new PostgresAuthTestFactory(
                socialSeeds: new[]
                {
                    first,
                    second
                });

        await factory.PrepareDatabaseAsync();

        await factory.SeedUserAsync(
            firstEmail,
            emailConfirmed: true);

        await factory.SeedUserAsync(
            secondEmail,
            emailConfirmed: true);

        var results = await Task.WhenAll(
            SendSocialOutcomeAsync(
                factory,
                first.RawToken),

            SendSocialOutcomeAsync(
                factory,
                second.RawToken));

        var successfulRequests =
            results.Count(success => success);

        // Depending on timing:
        //
        // 1 success:
        // both requests race to create the same provider link and
        // one loses at the Identity database constraint.
        //
        // 2 successes:
        // one request commits first, then the second request sees
        // the already-linked provider identity and signs it in.
        Assert.InRange(
            successfulRequests,
            1,
            2);

        Assert.Equal(
            1,
            await AuthDbAssertions.LoginCountAsync(
                factory,
                Provider,
                providerId));

        Assert.Equal(
            1,
            await AuthDbAssertions.UserCountByEmailAsync(
                factory,
                firstEmail));

        Assert.Equal(
            1,
            await AuthDbAssertions.UserCountByEmailAsync(
                factory,
                secondEmail));
    }

    private static PostgresAuthTestFactory Factory(
        SocialIdentitySeed seed,
        AuthFaultPlan? faults = null)
        => new(
            faults,
            new[]
            {
                seed
            });

    private static SocialIdentitySeed Seed(
        string tokenSuffix,
        string email,
        string? providerId = null)
        => new(
            RawToken:
                $"token-{tokenSuffix}-{Guid.NewGuid():N}",

            ProviderName:
                Provider,

            ProviderId:
                providerId ??
                $"provider-{Guid.NewGuid():N}",

            Email:
                email,

            IsEmailVerified:
                true);

    private static string UniqueEmail(
        string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@example.test";

    private static Task<object?> SendSocialAsync(
        PostgresAuthTestFactory factory,
        string rawToken)
        => factory.InScopeAsync(
            services =>
                AuthTestReflection.SendAsync(
                    services,
                    AuthTestReflection.Create<
                        SocialLoginCommand>(
                        new Dictionary<string, object?>(
                            StringComparer.OrdinalIgnoreCase)
                        {
                            ["GoogleIdToken"] =
                                rawToken,

                            ["AppleIdentityToken"] =
                                null,

                            ["IpAddress"] =
                                "127.0.0.1"
                        })));

    private static async Task<bool> SendSocialOutcomeAsync(
        PostgresAuthTestFactory factory,
        string rawToken)
    {
        try
        {
            var result =
                await SendSocialAsync(
                    factory,
                    rawToken);

            return AuthTestReflection.IsSucceeded(
                result);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<int>
        UserAccountCountByEmailAsync(
            PostgresAuthTestFactory factory,
            string email)
    {
        return await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<
                        AppDbContext>();

                var normalizedEmail =
                    email.ToUpperInvariant();

                return await (
                    from user in db.Users.AsNoTracking()
                    join account in db.UserAccounts.AsNoTracking()
                        on user.Id equals account.Id
                    where user.NormalizedEmail == normalizedEmail
                    select account.Id)
                    .CountAsync();
            });
    }

    private static async Task<Guid?>
        UserIdByEmailAsync(
            PostgresAuthTestFactory factory,
            string email)
    {
        return await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<
                        AppDbContext>();

                var normalizedEmail =
                    email.ToUpperInvariant();

                return await db.Users
                    .AsNoTracking()
                    .Where(
                        user =>
                            user.NormalizedEmail ==
                            normalizedEmail)
                    .Select(
                        user =>
                            (Guid?)user.Id)
                    .SingleOrDefaultAsync();
            });
    }

    private static async Task
        AssertFailureOrExceptionAsync(
            Func<Task<object?>> action)
    {
        try
        {
            var result = await action();

            Assert.False(
                AuthTestReflection.IsSucceeded(result));
        }
        catch (InvalidOperationException ex)
            when (ex.Message.Contains(
                "Injected",
                StringComparison.OrdinalIgnoreCase))
        {
            // Expected injected infrastructure failure.
        }
    }
}