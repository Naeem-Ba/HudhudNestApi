using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using HudhudNestApi.Infrastructure.Health;
using HudhudNestApi.Infrastructure.Persistence;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Health;

/// <summary>
/// A deploy that ships a migration but skips the (manual) Migrator used to look healthy: the
/// database answered and PostGIS was there, while every query that selected a new column failed.
/// Readiness now says so. Runs on its own disposable database so it controls exactly which
/// migrations are applied.
/// </summary>
[Collection("AuthPostgres")]
public sealed class PendingMigrationsHealthCheckTests : IAsyncLifetime
{
    private string _adminConnectionString = string.Empty;
    private string _scratchDatabaseName = string.Empty;
    private string _scratchConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        // The API sets this at startup; without it EF reports pending model changes when this test runs alone.
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var baseConnectionString =
            Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? throw new InvalidOperationException(
                "A PostgreSQL test connection string is required. Set " +
                "TEST_POSTGRES_CONNECTION_STRING or ConnectionStrings__DefaultConnection.");

        _scratchDatabaseName = $"pending_migrations_{Guid.NewGuid():N}";
        _adminConnectionString = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            Database = "postgres"
        }.ConnectionString;

        await using (var admin = new NpgsqlConnection(_adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = admin.CreateCommand();
            create.CommandText = $"CREATE DATABASE \"{_scratchDatabaseName}\";";
            await create.ExecuteNonQueryAsync();
        }

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

    [Fact(DisplayName = "Readiness: a fully migrated database is healthy")]
    public async Task FullyMigrated_IsHealthy()
    {
        await using var context = CreateContext();
        await context.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync();

        var result = await new PendingMigrationsHealthCheck(context)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact(DisplayName = "Readiness: a database behind the build is unhealthy and the log names what is missing")]
    public async Task BehindTheBuild_IsUnhealthy()
    {
        await using var context = CreateContext();
        var migrator = context.GetInfrastructure().GetRequiredService<IMigrator>();
        var all = context.Database.GetMigrations().ToList();
        Assert.True(all.Count > 2);

        var olderOne = all[^3];
        await migrator.MigrateAsync(olderOne);

        var result = await new PendingMigrationsHealthCheck(context)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("2 migration(s) not applied", result.Description, StringComparison.Ordinal);
        Assert.Contains(all[^1], result.Description, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Readiness: an empty database (no migrations applied at all) is unhealthy")]
    public async Task NeverMigrated_IsUnhealthy()
    {
        await using var context = CreateContext();

        var result = await new PendingMigrationsHealthCheck(context)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact(DisplayName = "Readiness: an in-memory test host has no migration history and stays healthy")]
    public async Task InMemoryProvider_IsHealthy()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"pending-migrations-{Guid.NewGuid():N}")
            .Options;
        await using var context = new AppDbContext(options);

        var result = await new PendingMigrationsHealthCheck(context)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_scratchConnectionString)
            .Options;
        return new AppDbContext(options);
    }
}
