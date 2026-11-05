using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using HudhudNestApi.Infrastructure.Health;
using HudhudNestApi.Infrastructure.Persistence;
using Xunit;

namespace HudhudNestApi.Integration.Tests.Health;

/// <summary>
/// The Supabase advisories rls_disabled_in_public and sensitive_columns_exposed came back because
/// protection depended on a manual migration plus an event trigger that Supabase may refuse. The
/// enforcer and the readiness check must not depend on either: these tests switch RLS off by hand
/// (as a table created without the trigger would be) and prove the enforcer closes it and the check
/// notices it. Runs on its own disposable database.
/// </summary>
[Collection("AuthPostgres")]
public sealed class RowLevelSecurityTests : IAsyncLifetime
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

        _scratchDatabaseName = $"row_level_security_{Guid.NewGuid():N}";
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

    [Fact(DisplayName = "RLS: every public table of a fully migrated database has row-level security")]
    public async Task FullyMigrated_HasNoUnprotectedTable()
    {
        await using var context = await CreateMigratedContextAsync();

        var unprotected = await RowLevelSecurityEnforcer.FindUnprotectedTablesAsync(context.Database);

        Assert.Empty(unprotected);
    }

    [Fact(DisplayName = "RLS: a table left open is closed by the enforcer, without relying on the event trigger")]
    public async Task Enforcer_ClosesATableThatLostRowLevelSecurity()
    {
        await using var context = await CreateMigratedContextAsync();
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"AgencyRatings\" DISABLE ROW LEVEL SECURITY;");
        await context.Database.ExecuteSqlRawAsync("CREATE TABLE public.\"CreatedWithoutTrigger\" (\"Id\" integer);");
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"CreatedWithoutTrigger\" DISABLE ROW LEVEL SECURITY;");

        var before = await RowLevelSecurityEnforcer.FindUnprotectedTablesAsync(context.Database);
        Assert.Contains("AgencyRatings", before);
        Assert.Contains("CreatedWithoutTrigger", before);

        var remaining = await RowLevelSecurityEnforcer.EnforceAsync(context.Database);

        Assert.Empty(remaining);
        Assert.Empty(await RowLevelSecurityEnforcer.FindUnprotectedTablesAsync(context.Database));
    }

    [Fact(DisplayName = "RLS: the enforcer is idempotent on an already protected database")]
    public async Task Enforcer_IsIdempotent()
    {
        await using var context = await CreateMigratedContextAsync();

        Assert.Empty(await RowLevelSecurityEnforcer.EnforceAsync(context.Database));
        Assert.Empty(await RowLevelSecurityEnforcer.EnforceAsync(context.Database));
    }

    [Fact(DisplayName = "RLS readiness: an open table degrades the check, a closed schema is healthy")]
    public async Task HealthCheck_ReportsOpenTables()
    {
        await using var context = await CreateMigratedContextAsync();
        var check = new RowLevelSecurityHealthCheck(context, NullLogger<RowLevelSecurityHealthCheck>.Instance);

        var healthy = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Healthy, healthy.Status);

        await context.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"AgencyRatings\" DISABLE ROW LEVEL SECURITY;");

        var degraded = await check.CheckHealthAsync(new HealthCheckContext());
        Assert.Equal(HealthStatus.Degraded, degraded.Status);
        Assert.DoesNotContain("AgencyRatings", degraded.Description, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "RLS readiness: an in-memory test host stays healthy")]
    public async Task HealthCheck_InMemoryProvider_IsHealthy()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"rls-{Guid.NewGuid():N}")
            .Options;
        await using var context = new AppDbContext(options);

        var result = await new RowLevelSecurityHealthCheck(context, NullLogger<RowLevelSecurityHealthCheck>.Instance)
            .CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private async Task<AppDbContext> CreateMigratedContextAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_scratchConnectionString)
            .Options;
        var context = new AppDbContext(options);
        await context.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync();
        return context;
    }
}
