using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Health;

/// <summary>
/// Unhealthy while the database is behind the migrations this build ships with.
///
/// The API never migrates itself: the Migrator runs by hand before a deploy. A deploy without it
/// used to look perfectly healthy (the database answered, PostGIS was there) while every query
/// that selected a new column failed with 42703 and the app showed "server error" on its first
/// screen. The anonymous answer stays status-only (see HealthCheckResponseWriter), so the names of
/// the missing migrations go to the log, never to the caller.
/// </summary>
public sealed class PendingMigrationsHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public PendingMigrationsHealthCheck(AppDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Test hosts run on an in-memory provider that has no migration history.
        if (!_db.Database.IsRelational())
        {
            return HealthCheckResult.Healthy("The database provider has no migration history.");
        }

        try
        {
            var pending = (await _db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();

            return pending.Length == 0
                ? HealthCheckResult.Healthy("The database schema is up to date.")
                : HealthCheckResult.Unhealthy(
                    $"{pending.Length} migration(s) not applied; run the Migrator before this build serves traffic: " +
                    string.Join(", ", pending));
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("The migration history could not be read.", ex);
        }
    }
}
