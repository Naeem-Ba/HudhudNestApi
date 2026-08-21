using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropertyApi.Infrastructure.Identity.Entities;

namespace PropertyApi.Infrastructure.Persistence.Seeds;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var context =
            services.GetRequiredService<AppDbContext>();

        var roleManager =
            services.GetRequiredService<
                RoleManager<ApplicationRole>>();

        var logger = services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("DatabaseSeeder");

        var providerName =
            context.Database.ProviderName ?? string.Empty;

        var isPostgres = providerName.Contains(
            "Npgsql",
            StringComparison.OrdinalIgnoreCase);

        if (isPostgres)
        {
            var strategy =
                context.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction =
                    await context.Database.BeginTransactionAsync(
                        cancellationToken);

                await context.Database.ExecuteSqlRawAsync(
                    "SELECT pg_advisory_xact_lock(20260621194421::bigint);",
                    cancellationToken);

                logger.LogInformation(
                    "Database seed lock acquired.");

                await CurrencySeed.SeedAsync(context);
                await GovernoratesSeed.SeedAsync(context);
                await NeighborhoodsSeed.SeedAsync(context);
                await PropertyTypesSeed.SeedAsync(context);
                await ApplicationRolesSeed.SeedAsync(roleManager);

                await transaction.CommitAsync(
                    cancellationToken);

                logger.LogInformation(
                    "Database seed completed.");
            });

            return;
        }

        await CurrencySeed.SeedAsync(context);
        await GovernoratesSeed.SeedAsync(context);
        await NeighborhoodsSeed.SeedAsync(context);
        await PropertyTypesSeed.SeedAsync(context);
        await ApplicationRolesSeed.SeedAsync(roleManager);
    }
}