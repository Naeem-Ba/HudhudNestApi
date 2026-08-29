using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PropertyApi.Application.Auth.Interfaces;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Integration.Tests.Auth;

internal static class AuthDbAssertions
{
    public static async Task<int> UserCountByEmailAsync(
        PostgresAuthTestFactory factory,
        string email)
        => await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                var normalized =
                    email.Trim().ToUpperInvariant();

                return await db.Users
                    .AsNoTracking()
                    .CountAsync(
                        user =>
                            user.NormalizedEmail ==
                            normalized);
            });

    public static async Task<int> UserCountByPhoneAsync(
        PostgresAuthTestFactory factory,
        string phoneNumber)
        => await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                var hasher =
                    services.GetRequiredService<
                        IPhoneNumberLookupHasher>();

                var hash =
                    hasher.Compute(phoneNumber);

                return await db.Users
                    .AsNoTracking()
                    .CountAsync(
                        user =>
                            user.PhoneNumberLookupHash ==
                            hash);
            });

    public static async Task<
        (Guid UserId, bool PhoneConfirmed)?>
        UserByPhoneAsync(
            PostgresAuthTestFactory factory,
            string phoneNumber)
        => await factory.InScopeAsync<
            (Guid UserId, bool PhoneConfirmed)?>(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                var hasher =
                    services.GetRequiredService<
                        IPhoneNumberLookupHasher>();

                var hash =
                    hasher.Compute(phoneNumber);

                var user =
                    await db.Users
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            user =>
                                user.PhoneNumberLookupHash ==
                                hash);

                return user is null
                    ? null
                    : (
                        UserId: user.Id,
                        PhoneConfirmed:
                            user.PhoneNumberConfirmed);
            });

    public static async Task<int> UserAccountCountAsync(
        PostgresAuthTestFactory factory,
        Guid userId)
        => await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                return await db.UserAccounts
                    .AsNoTracking()
                    .CountAsync(
                        account =>
                            account.Id == userId);
            });

    public static async Task<int>
        UserAccountTotalCountAsync(
            PostgresAuthTestFactory factory)
        => await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                return await db.UserAccounts
                    .AsNoTracking()
                    .CountAsync();
            });

    public static async Task<int> RoleCountAsync(
        PostgresAuthTestFactory factory,
        Guid userId)
        => await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                return await db.UserRoles
                    .AsNoTracking()
                    .CountAsync(
                        role =>
                            role.UserId == userId);
            });

    public static async Task<int> LoginCountAsync(
        PostgresAuthTestFactory factory,
        string provider,
        string providerId)
        => await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<AppDbContext>();

                return await db.UserLogins
                    .AsNoTracking()
                    .CountAsync(
                        login =>
                            login.LoginProvider ==
                            provider &&
                            login.ProviderKey ==
                            providerId);
            });
}
