using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PropertyApi.Infrastructure;
using PropertyApi.Infrastructure.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.json",
        optional: true,
        reloadOnChange: false)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services.AddInfrastructure(
    builder.Configuration,
    builder.Environment);

using var host = builder.Build();

var logger = host.Services
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("PropertyApi.Migrator");

await using var scope = host.Services.CreateAsyncScope();

var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

try
{
    logger.LogInformation("Starting database migration...");

    var pendingMigrations = await db.Database
        .GetPendingMigrationsAsync();

    var pendingMigrationList = pendingMigrations.ToList();

    if (pendingMigrationList.Count == 0)
    {
        logger.LogInformation("No pending migrations found. Database is already up to date.");
        return 0;
    }

    logger.LogInformation(
        "Found {Count} pending migration(s): {Migrations}",
        pendingMigrationList.Count,
        string.Join(", ", pendingMigrationList));

    var migrator = db.Database
        .GetInfrastructure()
        .GetRequiredService<IMigrator>();

    await migrator.MigrateAsync();

    logger.LogInformation("Database migration completed successfully.");
    return 0;
}
catch (Exception ex)
{
    logger.LogError(ex, "Database migration failed.");

    /*
     * Rollback policy:
     * EF Core migrations should not be automatically rolled back here.
     * In production, rollback should be handled by deployment tooling:
     *
     * 1. Stop deployment / mark release as failed.
     * 2. Restore database backup if schema migration is destructive.
     * 3. Redeploy previous application version.
     * 4. Run explicit rollback migration only if it was reviewed.
     */

    return 1;
}