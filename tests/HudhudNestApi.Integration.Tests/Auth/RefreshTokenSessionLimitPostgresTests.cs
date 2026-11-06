using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using HudhudNestApi.Infrastructure.Auth.Repositories;
using HudhudNestApi.Infrastructure.Identity.Services;
using HudhudNestApi.Infrastructure.Persistence;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Auth;

/// <summary>
/// A replay of a rotated refresh token inside the grace window hands out a sibling session. Against the real
/// (PostgreSQL) repository this must never push the live sessions past Jwt:MaxActiveSessions, otherwise one
/// stolen token could be replayed into any number of live sessions.
/// </summary>
[Collection("AuthPostgres")]
public sealed class RefreshTokenSessionLimitPostgresTests : IAsyncLifetime
{
    private string _adminConnectionString = string.Empty;
    private string _scratchDatabaseName = string.Empty;
    private string _scratchConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var baseConnectionString =
            Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException(
                "A PostgreSQL test connection string is required. Set " +
                "TEST_POSTGRES_CONNECTION_STRING or ConnectionStrings__DefaultConnection.");

        _scratchDatabaseName = $"refresh_limit_{Guid.NewGuid():N}";
        _adminConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString) { Database = "postgres" }.ConnectionString;

        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();
        await using var create = admin.CreateCommand();
        create.CommandText = $"CREATE DATABASE \"{_scratchDatabaseName}\";";
        await create.ExecuteNonQueryAsync();

        _scratchConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = _scratchDatabaseName
        }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(_adminConnectionString);
        await admin.OpenAsync();
        await using var drop = admin.CreateCommand();
        drop.CommandText = $"DROP DATABASE IF EXISTS \"{_scratchDatabaseName}\" WITH (FORCE);";
        await drop.ExecuteNonQueryAsync();
    }

    [Fact(DisplayName = "Refresh grace: sibling sessions from replays never exceed Jwt:MaxActiveSessions")]
    public async Task RotatedReplays_NeverExceedTheSessionLimit()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_scratchConnectionString).Options;
        await using var context = new AppDbContext(options);
        await context.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync();

        // The user row is irrelevant to the rule under test; the FK is bypassed for this scratch database only.
        await context.Database.OpenConnectionAsync();
        await context.Database.ExecuteSqlRawAsync("SET session_replication_role = replica;");

        var jwt = Options.Create(new JwtOptions { MaxActiveSessions = 3, RefreshReuseGraceSeconds = 60 });
        var repository = new RefreshTokenRepository(context, jwt, NullLogger<RefreshTokenRepository>.Instance);
        var userId = Guid.NewGuid();

        await repository.AddAsync(userId, "first-sign-in-token", "198.51.100.1");

        for (var replay = 0; replay < 10; replay++)
        {
            await repository.AddRotatedAsync(userId, $"sibling-token-{replay}", "198.51.100.1");
        }

        var now = DateTime.UtcNow;
        var live = await context.RefreshTokens.IgnoreQueryFilters()
            .Where(t => t.UserId == userId && !t.IsRevoked && t.ExpiresAt > now)
            .CountAsync();
        Assert.Equal(3, live);

        // The newest sibling is always one of the survivors: handing out a token never ends that same token.
        var newest = await context.RefreshTokens.IgnoreQueryFilters()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .FirstAsync();
        Assert.False(newest.IsRevoked);
    }

    [Fact(DisplayName = "Refresh grace: concurrent replays of one rotated token never exceed Jwt:MaxActiveSessions")]
    public async Task ConcurrentReplaysOfOneToken_NeverExceedTheSessionLimit()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_scratchConnectionString).Options;

        await using (var setup = new AppDbContext(options))
        {
            await setup.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync();
        }

        var jwt = Options.Create(new JwtOptions { MaxActiveSessions = 5, RefreshReuseGraceSeconds = 60 });
        var userId = Guid.NewGuid();

        // Same shape as RefreshTokenCommandHandler: one transaction per request that reads the token, tries
        // to rotate it and hands out its own new token (the winner rotates, the losers get a grace sibling).
        async Task SeedAsync()
        {
            await using var context = new AppDbContext(options);
            await context.Database.OpenConnectionAsync();
            await context.Database.ExecuteSqlRawAsync("SET session_replication_role = replica;");
            var repository = new RefreshTokenRepository(context, jwt, NullLogger<RefreshTokenRepository>.Instance);
            await repository.AddAsync(userId, "shared-token", "198.51.100.1");
        }

        async Task RefreshAsync(int caller)
        {
            await using var context = new AppDbContext(options);
            await context.Database.OpenConnectionAsync();
            await context.Database.ExecuteSqlRawAsync("SET session_replication_role = replica;");
            var repository = new RefreshTokenRepository(context, jwt, NullLogger<RefreshTokenRepository>.Instance);

            await repository.ExecuteInTransactionAsync(async ct =>
            {
                var stored = await repository.GetByRefreshTokenAsync("shared-token", ct);
                Assert.NotNull(stored);

                // A caller that arrives after the winner committed sees the token already rotated and, inside
                // the grace window, skips straight to handing out its own sibling.
                if (!stored!.IsRevoked)
                {
                    await repository.RevokeIfActiveAsync(
                        stored.Id, DateTime.UtcNow, "198.51.100.1", $"sibling-{caller}", ct);
                }

                await repository.AddRotatedAsync(userId, $"sibling-{caller}", "198.51.100.1", ct);
                return true;
            });
        }

        const int concurrency = 10;

        for (var round = 0; round < 5; round++)
        {
            await using (var reset = new AppDbContext(options))
            {
                await reset.Database.ExecuteSqlRawAsync("DELETE FROM \"RefreshTokens\";");
            }

            await SeedAsync();

            // The winner rotates first; the rest then replay the already rotated token at the same time.
            await RefreshAsync(-1);

            using var start = new ManualResetEventSlim(false);
            var callers = Enumerable.Range(0, concurrency)
                .Select(i => Task.Run(async () =>
                {
                    start.Wait();
                    await RefreshAsync(i);
                }))
                .ToArray();

            start.Set();
            await Task.WhenAll(callers);

            await using var verify = new AppDbContext(options);
            var now = DateTime.UtcNow;
            var live = await verify.RefreshTokens.IgnoreQueryFilters()
                .Where(t => t.UserId == userId && !t.IsRevoked && t.ExpiresAt > now)
                .CountAsync();

            Assert.True(live <= 5, $"round {round}: {live} live sessions after {concurrency} concurrent refreshes of one token.");
        }
    }
}
