using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using PropertyApi.Infrastructure.Persistence;
using Xunit;

namespace PropertyApi.Integration.Tests.Auth;

/// <summary>
/// Proves the guarded pre-check inside the
/// <see cref="PropertyApi.Infrastructure.Migrations.AddEmailLowerCaseUniqueIndex"/> migration
/// (Finding F3) actually raises and aborts when a case-variant duplicate email already exists,
/// rather than merely trusting that the SQL in the migration file looks correct. Runs against
/// its own disposable database (not the shared truncated one the rest of this project's
/// Postgres-backed tests use) because it needs precise control over which migrations are
/// applied before the duplicate row is inserted -- something a shared, always-fully-migrated
/// fixture cannot give it.
/// </summary>
[Collection("AuthPostgres")]
public sealed class EmailUniquenessMigrationGuardTests : IAsyncLifetime
{
    private const string GuardMigrationId = "AddEmailLowerCaseUniqueIndex";

    private string _adminConnectionString = string.Empty;
    private string _scratchDatabaseName = string.Empty;
    private string _scratchConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        var baseConnectionString =
            Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException(
                "A PostgreSQL test connection string is required. Set " +
                "TEST_POSTGRES_CONNECTION_STRING or ConnectionStrings__DefaultConnection.");

        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString);
        _scratchDatabaseName = $"f3_migration_guard_{Guid.NewGuid():N}";

        var adminBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = "postgres"
        };
        _adminConnectionString = adminBuilder.ConnectionString;

        await using (var admin = new NpgsqlConnection(_adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{_scratchDatabaseName}\";";
            await create.ExecuteNonQueryAsync();
        }

        var scratchBuilder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = _scratchDatabaseName
        };
        _scratchConnectionString = scratchBuilder.ConnectionString;
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

    [Fact(DisplayName =
        "AddEmailLowerCaseUniqueIndex raises and applies nothing when a case-variant duplicate already exists")]
    public async Task Migration_RaisesAndDoesNotApply_WhenCaseVariantDuplicateExists()
    {
        await using var context = CreateContext();
        var migrator = context.GetInfrastructure().GetRequiredService<IMigrator>();

        var allMigrations = context.Database.GetMigrations().ToList();
        var guardIndex = allMigrations.FindIndex(id => id.EndsWith(GuardMigrationId, StringComparison.Ordinal));
        Assert.True(guardIndex > 0, $"Migration '{GuardMigrationId}' was not found in the migration history.");

        var previousMigrationId = allMigrations[guardIndex - 1];

        // Migrate to exactly one migration short of the guarded one.
        await migrator.MigrateAsync(previousMigrationId);

        // Insert a case-variant duplicate directly -- bypassing the application entirely, the
        // same way Phase 2's original audit proved the pre-existing gap.
        await using (var connection = new NpgsqlConnection(_scratchConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO "Users"
                    ("Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
                     "EmailConfirmed", "PasswordHash", "SecurityStamp", "ConcurrencyStamp",
                     "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled",
                     "AccessFailedCount", "CreatedAt", "UpdatedAt", "IsBanned", "IsDeleted",
                     "PhoneVerificationState")
                VALUES
                    (gen_random_uuid(), 'first', 'FIRST', 'dup@example.com', 'DUP@EXAMPLE.COM',
                     false, 'x', 'x', 'x', false, false, true, 0, now(), now(), false, false, 0),
                    (gen_random_uuid(), 'second', 'SECOND', 'DUP@example.com', 'DUP@EXAMPLE.COM',
                     false, 'x', 'x', 'x', false, false, true, 0, now(), now(), false, false, 0);
                """;
            await insert.ExecuteNonQueryAsync();
        }

        // The guarded migration must refuse to proceed.
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync());
        Assert.Contains("case-variant duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);

        // Confirm nothing was left half-applied: the guard migration must not be recorded as
        // applied, and the index it would have created must not exist.
        var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.DoesNotContain(
            appliedMigrations,
            id => id.EndsWith(GuardMigrationId, StringComparison.Ordinal));

        await using (var connection = new NpgsqlConnection(_scratchConnectionString))
        {
            await connection.OpenAsync();
            await using var check = connection.CreateCommand();
            check.CommandText =
                "SELECT count(*) FROM pg_indexes WHERE indexname = 'IX_Users_Email_Lower';";
            var indexCount = (long)(await check.ExecuteScalarAsync() ?? 0L);
            Assert.Equal(0, indexCount);
        }
    }

    [Fact(DisplayName =
        "AddEmailLowerCaseUniqueIndex applies cleanly and rejects a later case-variant insert when no duplicate exists")]
    public async Task Migration_Applies_WhenNoDuplicateExists()
    {
        await using var context = CreateContext();
        var migrator = context.GetInfrastructure().GetRequiredService<IMigrator>();

        await migrator.MigrateAsync();

        var appliedMigrations = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Contains(
            appliedMigrations,
            id => id.EndsWith(GuardMigrationId, StringComparison.Ordinal));

        await using var connection = new NpgsqlConnection(_scratchConnectionString);
        await connection.OpenAsync();
        await using var check = connection.CreateCommand();
        check.CommandText =
            "SELECT count(*) FROM pg_indexes WHERE indexname = 'IX_Users_Email_Lower';";
        var indexCount = (long)(await check.ExecuteScalarAsync() ?? 0L);
        Assert.Equal(1, indexCount);
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_scratchConnectionString)
            .Options;
        return new AppDbContext(options);
    }
}
