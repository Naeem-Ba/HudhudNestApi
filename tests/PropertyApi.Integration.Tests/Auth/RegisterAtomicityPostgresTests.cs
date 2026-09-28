using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Infrastructure.Persistence;
using Xunit;

namespace PropertyApi.Integration.Tests.Auth;

[Collection("AuthPostgres")]
public sealed class RegisterAtomicityPostgresTests
{
    private const string Password = "StrongPass!123";

    [Fact]
    public async Task CreateAsyncFailure_LeavesNoUserOrUserAccount()
    {
        await using var factory =
            new PostgresAuthTestFactory(
                new AuthFaultPlan
                {
                    FailCreateAsync = true
                });

        await factory.PrepareDatabaseAsync();

        var email = UniqueEmail("create-fail");

        var result = await factory.InScopeAsync(
            services =>
                AuthTestReflection.SendAsync(
                    services,
                    AuthTestReflection.RegisterCommand(
                        email,
                        Password)));

        Assert.False(
            AuthTestReflection.IsSucceeded(result));

        Assert.Equal(
            0,
            await AuthDbAssertions.UserCountByEmailAsync(
                factory,
                email));

        Assert.Equal(
            0,
            await UserAccountCountAsync(factory));
    }

    [Fact]
    public async Task RoleFailure_RollsBackCreatedUserAndUserAccount()
    {
        await using var factory =
            new PostgresAuthTestFactory(
                new AuthFaultPlan
                {
                    FailAddToRoleAsync = true
                });

        await factory.PrepareDatabaseAsync();

        var email = UniqueEmail("role-fail");

        var result = await factory.InScopeAsync(
            services =>
                AuthTestReflection.SendAsync(
                    services,
                    AuthTestReflection.RegisterCommand(
                        email,
                        Password)));

        Assert.False(
            AuthTestReflection.IsSucceeded(result));

        Assert.Equal(
            0,
            await AuthDbAssertions.UserCountByEmailAsync(
                factory,
                email));

        Assert.Equal(
            0,
            await UserAccountCountAsync(factory));
    }

    [Fact]
    public async Task Success_CreatesOneUserAccountWithExactlyOneRole()
    {
        await using var factory =
            new PostgresAuthTestFactory();

        await factory.PrepareDatabaseAsync();

        var email = UniqueEmail("success");

        var result = await factory.InScopeAsync(
            services =>
                AuthTestReflection.SendAsync(
                    services,
                    AuthTestReflection.RegisterCommand(
                        email,
                        Password)));

        Assert.True(
            AuthTestReflection.IsSucceeded(result));

        Assert.Equal(
            1,
            await AuthDbAssertions.UserCountByEmailAsync(
                factory,
                email));

        var user = await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                return await db.Users
                    .AsNoTracking()
                    .SingleAsync(
                        user =>
                            user.NormalizedEmail ==
                            email.ToUpperInvariant());
            });

        Assert.Equal(
            1,
            await AuthDbAssertions.RoleCountAsync(
                factory,
                user.Id));

        var account = await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                return await db.UserAccounts
                    .AsNoTracking()
                    .SingleAsync(
                        account =>
                            account.Id == user.Id);
            });

        Assert.Equal(
            user.Id,
            account.Id);

        Assert.Equal(
            "Atomic",
            account.FirstName);

        Assert.Equal(
            "Test",
            account.LastName);

        Assert.Equal(
            1,
            await UserAccountCountAsync(factory));
    }

    [Fact]
    public async Task ConcurrentRequests_WithSameEmail_OnlyOneSucceeds_ByDatabaseUniqueConstraint()
    {
        await using var factory =
            new PostgresAuthTestFactory();

        await factory.PrepareDatabaseAsync();

        var email = UniqueEmail("concurrent");

        var first =
            AuthTestReflection.RegisterCommand(
                email,
                Password);

        var second =
            AuthTestReflection.RegisterCommand(
                email,
                Password);

        var results = await Task.WhenAll(
            SendOutcomeAsync(
                factory,
                first),
            SendOutcomeAsync(
                factory,
                second));

        Assert.Equal(
            1,
            results.Count(success => success));

        Assert.Equal(
            1,
            await AuthDbAssertions.UserCountByEmailAsync(
                factory,
                email));

        Assert.Equal(
            1,
            await UserAccountCountAsync(factory));

        var user = await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                return await db.Users
                    .AsNoTracking()
                    .SingleAsync(
                        user =>
                            user.NormalizedEmail ==
                            email.ToUpperInvariant());
            });

        Assert.Equal(
            1,
            await AuthDbAssertions.RoleCountAsync(
                factory,
                user.Id));

        var accountExists = await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                return await db.UserAccounts
                    .AsNoTracking()
                    .AnyAsync(
                        account =>
                            account.Id == user.Id);
            });

        Assert.True(accountExists);
    }

    private static async Task<bool> SendOutcomeAsync(
        PostgresAuthTestFactory factory,
        object command)
    {
        // No catch: the loser of the race must come back as a conflict result, not as an
        // exception (which the API would surface as HTTP 500). An exception here fails the test.
        var result = await factory.InScopeAsync(
            services =>
                AuthTestReflection.SendAsync(
                    services,
                    command));

        return AuthTestReflection.IsSucceeded(result);
    }

    private static async Task<int> UserAccountCountAsync(
        PostgresAuthTestFactory factory)
    {
        return await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                return await db.UserAccounts
                    .AsNoTracking()
                    .CountAsync();
            });
    }

    private static string UniqueEmail(
        string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@example.test";
}