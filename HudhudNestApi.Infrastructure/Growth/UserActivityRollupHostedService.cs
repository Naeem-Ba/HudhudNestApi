using HudhudNestApi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace HudhudNestApi.Infrastructure.Growth;

/// <summary>
/// Growth engine P10: turns sessions into "active days" for retention. Every sign-in (password, phone, Google,
/// Apple) and every session refresh writes a refresh-token row; once an hour this job writes one
/// <c>user_active</c> growth event per account per UTC day seen in the last 3 days. The idempotency key makes a
/// re-run write nothing new. The sign-in path itself is not touched. Refresh-token rows are deleted 30 days
/// after they expire, so the event log is what keeps the history.
/// </summary>
public sealed class UserActivityRollupHostedService : BackgroundService
{
    internal static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    internal static readonly TimeSpan LookBack = TimeSpan.FromDays(3);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UserActivityRollupHostedService> _logger;

    public UserActivityRollupHostedService(IServiceScopeFactory scopeFactory, ILogger<UserActivityRollupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "User activity rollup failed.");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    internal async Task<int> RunOnceAsync(DateTime nowUtc, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var written = 0;

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await BackgroundJobLock.TryRunAsync(
                db.Database.GetDbConnection(),
                BackgroundJobLockKeys.UserActivityRollup,
                nameof(UserActivityRollupHostedService),
                _logger,
                async () => written = await db.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "GrowthEvents" ("Name", "OccurredAt", "UserId", "IdempotencyKey")
                    SELECT 'user_active', MIN(t."CreatedAt"), t."UserId",
                           'user_active:' || t."UserId"::text || ':' || to_char(t."CreatedAt", 'YYYYMMDD')
                    FROM "RefreshTokens" t
                    WHERE t."CreatedAt" >= @since AND t."CreatedAt" <= @now
                      AND EXISTS (SELECT 1 FROM "UserAccounts" u WHERE u."Id" = t."UserId")
                    GROUP BY t."UserId", to_char(t."CreatedAt", 'YYYYMMDD')
                    ON CONFLICT ("IdempotencyKey") DO NOTHING
                    """,
                    [
                        new NpgsqlParameter("since", NpgsqlDbType.Timestamp) { Value = nowUtc - LookBack },
                        new NpgsqlParameter("now", NpgsqlDbType.Timestamp) { Value = nowUtc },
                    ],
                    ct),
                ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        return written;
    }
}
