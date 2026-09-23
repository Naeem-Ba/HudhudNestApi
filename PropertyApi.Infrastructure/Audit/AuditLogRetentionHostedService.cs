using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Audit;

/// <summary>
/// Enforces a configurable retention window for the <c>AuditLogs</c> table.
///
/// <para>
/// Off by default, and deliberately so — <c>docs/privacy/privacy-gaps.md</c> (P1) found
/// this table had no retention or cleanup mechanism at all: every login, password change,
/// and phone-verification event has been accumulating since 2026-06-24 with no way to age
/// any of it out. Inventing a specific number of days here without the project owner's
/// decision would itself be an undocumented retention-policy change, so the sweep is a
/// no-op until BOTH <c>AuditLogRetention:Enabled</c> is set to <c>true</c> AND
/// <c>AuditLogRetention:RetentionDays</c> is set to a positive number of days — an operator
/// has to make both choices explicitly before this deletes a single row.
/// </para>
/// <para>
/// Pattern mirrors PhoneVerificationHostedService: IServiceScopeFactory + a periodic timer,
/// guarded by a Postgres advisory lock (BackgroundJobLockKeys.AuditLogRetention) so that of
/// several deployed instances running the same timer, only one actually sweeps per tick.
/// </para>
/// </summary>
public sealed class AuditLogRetentionHostedService : BackgroundService
{
    /// <summary>Retention is measured in days, so once a day is frequent enough.</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// Deleted per <c>ExecuteDeleteAsync</c> batch. Keeps any single statement (and the row
    /// lock it briefly holds) small even against a large pre-existing backlog on first
    /// enablement.
    /// </summary>
    private const int BatchSize = 500;

    /// <summary>
    /// Cap per sweep. A large backlog (retention enabled for the first time against years of
    /// accumulated rows) drains over several daily sweeps rather than one very long-running
    /// delete loop.
    /// </summary>
    private const int MaxRowsPerSweep = 5_000;

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AuditLogRetentionHostedService> _logger;

    public AuditLogRetentionHostedService(
        IServiceScopeFactory scopes,
        TimeProvider clock,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<AuditLogRetentionHostedService> logger)
    {
        _scopes = scopes;
        _clock = clock;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, _clock);
        do
        {
            if (_configuration.GetValue<bool>("AuditLogRetention:Enabled"))
            {
                try
                {
                    await SweepAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // A failed tick (database briefly unreachable, migration not applied yet, ...) must not
                    // escape ExecuteAsync: the default BackgroundServiceExceptionBehavior.StopHost would take
                    // the whole API process down. Log it and try again on the next tick.
                    _logger.LogError(ex, "Audit log retention sweep failed; will retry on the next tick.");
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// One sweep. Internal so a test can drive it directly instead of waiting on the timer.
    /// Returns the number of rows deleted (0 when disabled, misconfigured, or nothing was
    /// old enough).
    /// </summary>
    internal async Task<int> SweepAsync(CancellationToken ct)
    {
        var retentionDays = _configuration.GetValue<int?>("AuditLogRetention:RetentionDays");

        if (retentionDays is null || retentionDays <= 0)
        {
            _logger.LogWarning(
                "AuditLogRetention:Enabled is true but AuditLogRetention:RetentionDays is " +
                "missing or not a positive number of days ({RetentionDays}); skipping this " +
                "sweep rather than guessing a cutoff.",
                retentionDays);
            return 0;
        }

        var connectionString = PostgresConnectionStringResolver.Resolve(_configuration, _environment);
        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(ct);

        var deletedTotal = 0;

        try
        {
            await BackgroundJobLock.TryRunAsync(
                lockConnection,
                BackgroundJobLockKeys.AuditLogRetention,
                nameof(AuditLogRetentionHostedService),
                _logger,
                async () =>
                {
                    var cutoff = _clock.GetUtcNow().UtcDateTime.AddDays(-retentionDays.Value);

                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                    while (deletedTotal < MaxRowsPerSweep)
                    {
                        var deleted = await db.AuditLogs
                            .Where(a => a.Timestamp < cutoff)
                            .OrderBy(a => a.Timestamp)
                            .Take(BatchSize)
                            .ExecuteDeleteAsync(ct);

                        deletedTotal += deleted;

                        if (deleted < BatchSize)
                            break;
                    }

                    if (deletedTotal > 0)
                    {
                        _logger.LogInformation(
                            "Audit log retention sweep deleted {Count} row(s) older than " +
                            "{RetentionDays} day(s) (cutoff {Cutoff:O}).",
                            deletedTotal,
                            retentionDays.Value,
                            cutoff);
                    }
                },
                ct);
        }
        finally
        {
            await lockConnection.CloseAsync();
        }

        return deletedTotal;
    }
}
