using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.Health;

/// <summary>
/// Degraded while a table in the public schema has Row-Level Security switched off, which on
/// Supabase means the table is open to anyone holding the project URL and the anon key.
///
/// Degraded, not Unhealthy: the API is serving correctly and taking it out of rotation would turn a
/// data-exposure finding into an outage; the fix is to run the Migrator (it enforces RLS on every
/// run) or the idempotent script scripts/database/close-supabase-data-api.sql. The anonymous answer
/// stays status-only, so the table names go to the log, never to the caller.
/// </summary>
public sealed class RowLevelSecurityHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;
    private readonly ILogger<RowLevelSecurityHealthCheck> _logger;

    public RowLevelSecurityHealthCheck(AppDbContext db, ILogger<RowLevelSecurityHealthCheck> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Test hosts run on an in-memory provider that has no catalog.
        if (!_db.Database.IsNpgsql())
        {
            return HealthCheckResult.Healthy("The database provider has no row-level security.");
        }

        try
        {
            var unprotected = await RowLevelSecurityEnforcer.FindUnprotectedTablesAsync(
                _db.Database,
                cancellationToken);

            if (unprotected.Count == 0)
            {
                return HealthCheckResult.Healthy("Every public table has row-level security enabled.");
            }

            _logger.LogError(
                "{Count} public table(s) have row-level security disabled and are open to the Supabase Data API: {Tables}. " +
                "Run the Migrator or scripts/database/close-supabase-data-api.sql.",
                unprotected.Count,
                string.Join(", ", unprotected));

            return HealthCheckResult.Degraded(
                $"{unprotected.Count} public table(s) have row-level security disabled.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Row-level security status could not be read.", ex);
        }
    }
}
