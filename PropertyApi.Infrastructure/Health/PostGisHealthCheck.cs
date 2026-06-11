using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Health;

public sealed class PostGisHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public PostGisHealthCheck(AppDbContext db)
    {
        _db = db;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = _db.Database.GetDbConnection();

            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();

            command.CommandText = """
                SELECT
                    (CASE WHEN EXISTS (
                        SELECT 1
                        FROM pg_extension
                        WHERE extname = 'postgis'
                    ) THEN 1 ELSE 0 END)
                    +
                    (CASE WHEN EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_name = 'Properties'
                          AND column_name = 'GeoLocation'
                          AND udt_name = 'geography'
                    ) THEN 1 ELSE 0 END)
                    +
                    (CASE WHEN EXISTS (
                        SELECT 1
                        FROM pg_indexes
                        WHERE tablename = 'Properties'
                          AND indexname = 'IX_Properties_GeoLocation'
                    ) THEN 1 ELSE 0 END);
                """;

            var result = await command.ExecuteScalarAsync(cancellationToken);
            var checksPassed = Convert.ToInt32(result);

            return checksPassed == 3
                ? HealthCheckResult.Healthy("PostGIS extension, GeoLocation column, and GiST index are available.")
                : HealthCheckResult.Unhealthy(
                    $"PostGIS validation failed. Passed checks: {checksPassed}/3.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "PostGIS health check failed.",
                ex);
        }
    }
}