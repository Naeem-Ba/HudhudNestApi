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
}
