using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Infrastructure;
using HudhudNestApi.Infrastructure.Persistence;
using HudhudNestApi.Infrastructure.Persistence.Backfills;
using HudhudNestApi.Infrastructure.Persistence.Seeds;

var builder =
    Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile(
        "appsettings.json",
        optional: true,
        reloadOnChange: false)
    .AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.json",
        optional: true,
        reloadOnChange: false)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

var runPhoneLookupHashBackfill =
    string.Equals(
        builder.Configuration[
            "backfill-phone-lookup-hash"],
        "true",
        StringComparison.OrdinalIgnoreCase);

builder.Services.AddInfrastructure(
    builder.Configuration,
    builder.Environment);

// AddInfrastructure registers services whose dependencies live in the API host
// (IHubContext<NotificationHub>, IPhoneVerificationPolicy, IMediaFolderBuilder, ...).
// This tool only needs AppDbContext, the seeders and the backfill, so it must not
// validate the whole container: in the Development environment the host builds the
// provider with ValidateOnBuild and crashed before the first migration ran.
builder.ConfigureContainer(
    new DefaultServiceProviderFactory(
        new ServiceProviderOptions
        {
            ValidateOnBuild = false,
            ValidateScopes = false
        }));

using var host =
    builder.Build();

var logger =
    host.Services
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger(
            "HudhudNestApi.Migrator");

await using var scope =
    host.Services.CreateAsyncScope();

var services =
    scope.ServiceProvider;

var db =
    services.GetRequiredService<AppDbContext>();

try
{
    logger.LogInformation(
        "Starting database migration process.");

    var pendingMigrations =
        await db.Database
            .GetPendingMigrationsAsync();

    var pendingMigrationList =
        pendingMigrations.ToList();

    if (pendingMigrationList.Count == 0)
    {
        logger.LogInformation(
            "No pending migrations found. Database schema is already up to date.");
    }
    else
    {
        logger.LogInformation(
            "Found {Count} pending migration(s): {Migrations}",
            pendingMigrationList.Count,
            string.Join(
                ", ",
                pendingMigrationList));

        var migrator =
            db.Database
                .GetInfrastructure()
                .GetRequiredService<IMigrator>();

        await migrator.MigrateAsync();

        await DatabaseSeeder.SeedAsync(
    scope.ServiceProvider);

        logger.LogInformation(
            "Database schema migration completed successfully.");
    }

    if (runPhoneLookupHashBackfill)
    {
        logger.LogInformation(
            "PhoneNumberLookupHash backfill was explicitly requested.");

        var backfill =
            services.GetRequiredService<
                PhoneNumberLookupHashBackfill>();

        var result =
            await backfill.RunAsync();

        logger.LogInformation(
            "Phone lookup backfill result: " +
            "UsersWithPhone={UsersWithPhone}, " +
            "MissingBefore={MissingBefore}, " +
            "Updated={Updated}, " +
            "MissingAfter={MissingAfter}",
            result.UsersWithPhone,
            result.MissingBefore,
            result.Updated,
            result.MissingAfter);
    }
    else
    {
        logger.LogInformation(
            "PhoneNumberLookupHash backfill was not requested. Skipping.");
    }

    logger.LogInformation(
        "Migration process completed successfully.");

    return 0;
}
catch (OperationCanceledException)
{
    logger.LogWarning(
        "Migration process was cancelled.");

    return 2;
}
catch (Exception ex)
{
    logger.LogError(
        ex,
        "Database migration or backfill process failed.");

    return 1;
}