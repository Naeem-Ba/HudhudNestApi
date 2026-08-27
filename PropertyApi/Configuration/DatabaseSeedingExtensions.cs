using PropertyApi.Infrastructure.Persistence.Seeds;

namespace PropertyApi.Configuration;

public static class DatabaseSeedingExtensions
{
    // BUG FIX: DatabaseSeeder.SeedAsync existed in the codebase (governorates,
    // districts, neighborhoods, property types, currencies, roles) but was never
    // actually called from anywhere — dead code, which is the reason those lookup
    // tables were empty in practice. All the individual seed methods are
    // idempotent ("ensure exists" per row), so running this on every startup is
    // safe and cheap; it must NOT block the app from serving requests if it fails
    // (e.g. DB not migrated yet on first deploy), so failures are logged, not
    // thrown.
    public static async Task SeedReferenceDataAsync(this WebApplication app)
    {
        using var seedScope = app.Services.CreateScope();

        try
        {
            await DatabaseSeeder.SeedAsync(seedScope.ServiceProvider);
        }
        catch (Exception ex)
        {
            seedScope.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("DatabaseSeeder")
                .LogError(ex, "Reference-data seeding failed at startup — lookup tables " +
                    "(governorates/districts/neighborhoods/property types) may be incomplete " +
                    "until this is resolved and the app restarts.");
        }
    }
}
