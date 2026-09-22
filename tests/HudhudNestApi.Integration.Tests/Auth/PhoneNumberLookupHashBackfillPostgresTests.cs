using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HudhudNestApi.Application.Auth.Interfaces;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Persistence.Backfills;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Auth;

[Collection("AuthPostgres")]
public sealed class PhoneNumberLookupHashBackfillPostgresTests
{
    [Fact]
    public async Task LegacyUser_WithMissingHash_GetsCorrectLookupHash()
    {
        await using var factory =
            new PostgresAuthTestFactory();

        await factory.PrepareDatabaseAsync();

        var phone =
            UniquePhone();

        var user =
            await factory.SeedUserAsync(
                UniqueEmail("legacy"),
                phoneNumber: phone,
                phoneConfirmed: true);

        await ClearLookupHashAsync(
            factory,
            user.IdentityId);

        var result =
            await RunBackfillAsync(factory);

        Assert.Equal(
            1,
            result.MissingBefore);

        Assert.Equal(
            1,
            result.Updated);

        Assert.Equal(
            0,
            result.MissingAfter);

        var state =
            await ReadPhoneStateAsync(
                factory,
                user.IdentityId);

        Assert.Equal(
            phone,
            state.PhoneNumber);

        Assert.Equal(
            state.ExpectedHash,
            state.StoredHash);
    }

    [Fact]
    public async Task SecondExecution_IsIdempotent()
    {
        await using var factory =
            new PostgresAuthTestFactory();

        await factory.PrepareDatabaseAsync();

        var phone =
            UniquePhone();

        var user =
            await factory.SeedUserAsync(
                UniqueEmail("idempotent"),
                phoneNumber: phone,
                phoneConfirmed: true);

        await ClearLookupHashAsync(
            factory,
            user.IdentityId);

        var firstRun =
            await RunBackfillAsync(factory);

        var firstHash =
            (
                await ReadPhoneStateAsync(
                    factory,
                    user.IdentityId)
            ).StoredHash;

        var secondRun =
            await RunBackfillAsync(factory);

        var secondHash =
            (
                await ReadPhoneStateAsync(
                    factory,
                    user.IdentityId)
            ).StoredHash;

        Assert.Equal(
            1,
            firstRun.Updated);

        Assert.Equal(
            0,
            firstRun.MissingAfter);

        Assert.Equal(
            0,
            secondRun.MissingBefore);

        Assert.Equal(
            0,
            secondRun.Updated);

        Assert.Equal(
            0,
            secondRun.MissingAfter);

        Assert.NotNull(firstHash);

        Assert.Equal(
            firstHash,
            secondHash);
    }

    [Fact]
    public async Task DuplicatePhoneIdentity_StopsBeforeAnyWrite()
    {
        await using var factory =
            new PostgresAuthTestFactory();

        await factory.PrepareDatabaseAsync();

        var phone =
            UniquePhone();

        var firstUser =
            await factory.SeedUserAsync(
                UniqueEmail("duplicate-a"),
                phoneNumber: phone,
                phoneConfirmed: true);

        /*
         * Simulate a historical row before creating the second user.
         *
         * NULL values do not conflict with the unique lookup-hash index.
         */
        await ClearLookupHashAsync(
            factory,
            firstUser.IdentityId);

        var secondUser =
            await factory.SeedUserAsync(
                UniqueEmail("duplicate-b"),
                phoneNumber: phone,
                phoneConfirmed: true);

        await ClearLookupHashAsync(
            factory,
            secondUser.IdentityId);

        var exception =
            await Assert.ThrowsAsync<
                InvalidOperationException>(
                () =>
                    RunBackfillAsync(factory));

        Assert.Contains(
            "Duplicate or conflicting phone lookup identity",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);

        var firstHash =
            await ReadStoredHashAsync(
                factory,
                firstUser.IdentityId);

        var secondHash =
            await ReadStoredHashAsync(
                factory,
                secondUser.IdentityId);

        Assert.Null(firstHash);

        Assert.Null(secondHash);
    }

    [Fact]
    public async Task SoftDeletedLegacyUser_IsIncludedInBackfill()
    {
        await using var factory =
            new PostgresAuthTestFactory();

        await factory.PrepareDatabaseAsync();

        var phone =
            UniquePhone();

        var user =
            await factory.SeedUserAsync(
                UniqueEmail("deleted"),
                phoneNumber: phone,
                phoneConfirmed: true);

        await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<
                        AppDbContext>();

                await db.Users
                    .IgnoreQueryFilters()
                    .Where(
                        candidate =>
                            candidate.Id == user.IdentityId)
                    .ExecuteUpdateAsync(
                        setters =>
                            setters
                                .SetProperty(
                                    candidate =>
                                        candidate.IsDeleted,
                                    true)
                                .SetProperty(
                                    candidate =>
                                        candidate.PhoneNumberLookupHash,
                                    (string?)null));
            });

        var result =
            await RunBackfillAsync(factory);

        Assert.Equal(
            1,
            result.MissingBefore);

        Assert.Equal(
            1,
            result.Updated);

        Assert.Equal(
            0,
            result.MissingAfter);

        var state =
            await ReadPhoneStateAsync(
                factory,
                user.IdentityId);

        Assert.Equal(
            state.ExpectedHash,
            state.StoredHash);
    }

    private static async Task ClearLookupHashAsync(
        PostgresAuthTestFactory factory,
        Guid userId)
    {
        await factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<
                        AppDbContext>();

                await db.Users
                    .IgnoreQueryFilters()
                    .Where(
                        user =>
                            user.Id == userId)
                    .ExecuteUpdateAsync(
                        setters =>
                            setters.SetProperty(
                                user =>
                                    user.PhoneNumberLookupHash,
                                (string?)null)
                            .SetProperty(
                                user =>
                                    user.NormalizedPhoneNumber,
                                (string?)null));
            });
    }

    private static Task<
        PhoneNumberLookupHashBackfillResult>
        RunBackfillAsync(
            PostgresAuthTestFactory factory)
        => factory.InScopeAsync(
            async services =>
            {
                var backfill =
                    services.GetRequiredService<
                        PhoneNumberLookupHashBackfill>();

                return await backfill.RunAsync();
            });

    private static Task<
        (string PhoneNumber,
         string? StoredHash,
         string ExpectedHash)>
        ReadPhoneStateAsync(
            PostgresAuthTestFactory factory,
            Guid userId)
        => factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<
                        AppDbContext>();

                var hasher =
                    services.GetRequiredService<
                        IPhoneNumberLookupHasher>();

                var user =
                    await db.Users
                        .IgnoreQueryFilters()
                        .AsNoTracking()
                        .SingleAsync(
                            candidate =>
                                candidate.Id == userId);

                if (string.IsNullOrWhiteSpace(
                        user.PhoneNumber))
                {
                    throw new InvalidOperationException(
                        "Seeded test user has no phone number.");
                }

                var phoneNumber =
                    user.PhoneNumber;

                var expectedHash =
                    hasher.Compute(
                        phoneNumber);

                return (
                    PhoneNumber: phoneNumber,
                    StoredHash:
                        user.PhoneNumberLookupHash,
                    ExpectedHash:
                        expectedHash);
            });
    private static Task<string?> ReadStoredHashAsync(
        PostgresAuthTestFactory factory,
        Guid userId)
        => factory.InScopeAsync(
            async services =>
            {
                var db =
                    services.GetRequiredService<
                        AppDbContext>();

                return await db.Users
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(
                        user =>
                            user.Id == userId)
                    .Select(
                        user =>
                            user.PhoneNumberLookupHash)
                    .SingleAsync();
            });

    private static string UniquePhone()
    {
        var suffix =
            Random.Shared.Next(
                10_000_000,
                99_999_999);

        return $"+4916{suffix}";
    }

    private static string UniqueEmail(
        string prefix)
        => $"{prefix}-{Guid.NewGuid():N}@example.test";
}
