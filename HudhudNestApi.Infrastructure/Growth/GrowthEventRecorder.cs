using System.Diagnostics;
using HudhudNestApi.Application.Growth;
using HudhudNestApi.Domain.Growth.Entities;
using HudhudNestApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace HudhudNestApi.Infrastructure.Growth;

/// <summary>
/// Growth engine P4. One INSERT ... ON CONFLICT DO NOTHING per event: idempotent across retries and
/// concurrent requests, no read first, and it never touches the caller's tracked entities.
/// </summary>
public sealed class GrowthEventRecorder : IGrowthEventRecorder
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _time;
    private readonly ILogger<GrowthEventRecorder> _logger;

    public GrowthEventRecorder(AppDbContext db, TimeProvider time, ILogger<GrowthEventRecorder> logger)
    {
        _db = db;
        _time = time;
        _logger = logger;
    }

    public async Task RecordAsync(
        string name,
        string idempotencyKey,
        Guid? userId = null,
        Guid? propertyId = null,
        Guid? subjectId = null,
        AcquisitionTags? tags = null,
        CancellationToken ct = default)
    {
        try
        {
            var e = GrowthEvent.Create(
                name, idempotencyKey, _time.GetUtcNow().UtcDateTime, userId, propertyId, subjectId,
                Activity.Current?.TraceId.ToString(), tags?.Source, tags?.Medium, tags?.Campaign);

            // Typed parameters: an untyped null in INSERT ... SELECT cannot be inferred by PostgreSQL. The EXISTS
            // guard skips an event for an account deleted meanwhile instead of failing on the foreign key.
            await _db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "GrowthEvents"
                    ("Name", "OccurredAt", "UserId", "PropertyId", "SubjectId", "IdempotencyKey",
                     "CorrelationId", "Source", "Medium", "Campaign")
                SELECT @name, @at, @user, @property, @subject, @key, @correlation, @source, @medium, @campaign
                WHERE @user IS NULL OR EXISTS (SELECT 1 FROM "UserAccounts" WHERE "Id" = @user)
                ON CONFLICT ("IdempotencyKey") DO NOTHING
                """,
                [
                    Text("name", e.Name),
                    new NpgsqlParameter("at", NpgsqlDbType.Timestamp) { Value = e.OccurredAt },
                    Id("user", e.UserId),
                    Id("property", e.PropertyId),
                    Id("subject", e.SubjectId),
                    Text("key", e.IdempotencyKey),
                    Text("correlation", e.CorrelationId),
                    Text("source", e.Source),
                    Text("medium", e.Medium),
                    Text("campaign", e.Campaign),
                ],
                ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Growth event {Name} was not recorded.", name);
        }
    }

    private static NpgsqlParameter Text(string name, string? value) =>
        new(name, NpgsqlDbType.Varchar) { Value = (object?)value ?? DBNull.Value };

    private static NpgsqlParameter Id(string name, Guid? value) =>
        new(name, NpgsqlDbType.Uuid) { Value = value.HasValue ? value.Value : DBNull.Value };
}
