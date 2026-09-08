using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using PropertyApi.Application.Users.Commands.DeleteUser;
using PropertyApi.Application.Users.Interfaces;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Users;

/// <summary>
/// Executes deletion requests whose delay window (Finding F7,
/// docs/ACCOUNT-DELETION-PRODUCTION-READINESS.md) has matured. Pattern mirrors
/// <c>AuditLogRetentionHostedService</c> exactly: <see cref="IServiceScopeFactory"/> + a
/// periodic timer, guarded by a Postgres advisory lock
/// (<see cref="BackgroundJobLockKeys.AccountDeletionSweep"/>) so that of several deployed
/// instances running the same timer, only one actually executes a given sweep.
///
/// Unlike audit-log retention, this sweep is always on -- there is no equivalent "operator must
/// explicitly enable this" gate, because the behavior it drives (executing a deletion the
/// account owner explicitly requested, after the delay window they were shown) is not a new
/// retention policy being invented; it is the direct, necessary continuation of
/// RequestDeleteUserCommandHandler already having accepted that request.
/// </summary>
public sealed class AccountDeletionSweepHostedService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// Executed one account at a time (each with its own scope/transaction, exactly as
    /// DeleteUserCommandHandler already does for an interactive request) rather than batched,
    /// since anonymization is a multi-step Identity+UserAccount transaction, not a single bulk
    /// SQL statement like the audit-log retention sweep's delete. Capped per tick so a large
    /// backlog (e.g. this sweep disabled for a while, or a mass-deletion event) drains over
    /// several hourly ticks rather than one very long-running loop.
    /// </summary>
    private const int MaxAccountsPerSweep = 200;

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<AccountDeletionSweepHostedService> _logger;

    public AccountDeletionSweepHostedService(
        IServiceScopeFactory scopes,
        TimeProvider clock,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<AccountDeletionSweepHostedService> logger)
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
            await SweepAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// One sweep. Internal so a test can drive it directly instead of waiting on the timer.
    /// Returns the number of accounts executed.
    /// </summary>
    internal async Task<int> SweepAsync(CancellationToken ct)
    {
        var connectionString = PostgresConnectionStringResolver.Resolve(_configuration, _environment);
        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(ct);

        var executedCount = 0;

        try
        {
            await BackgroundJobLock.TryRunAsync(
                lockConnection,
                BackgroundJobLockKeys.AccountDeletionSweep,
                nameof(AccountDeletionSweepHostedService),
                _logger,
                async () =>
                {
                    var now = _clock.GetUtcNow().UtcDateTime;

                    using var scope = _scopes.CreateScope();
                    var accounts = scope.ServiceProvider.GetRequiredService<IUserAccountRepository>();

                    var dueAccountIds = await accounts.GetDueForDeletionAsync(now, MaxAccountsPerSweep, ct);

                    foreach (var accountId in dueAccountIds)
                    {
                        // Each account gets its own scope: DeleteUserCommandHandler's own
                        // transaction must not span multiple accounts, and a failure on one
                        // account must not roll back or block the others in this batch.
                        using var accountScope = _scopes.CreateScope();
                        var executor = accountScope.ServiceProvider.GetRequiredService<DeleteUserCommandHandler>();

                        try
                        {
                            var result = await executor.ExecuteScheduledDeletionAsync(accountId, ct);

                            if (result.Success)
                            {
                                executedCount++;
                            }
                            else
                            {
                                _logger.LogWarning(
                                    "Scheduled account deletion did not succeed for account {AccountId}: {Errors}",
                                    accountId,
                                    string.Join(", ", result.Errors));
                            }
                        }
                        catch (Exception ex)
                        {
                            // One account's unexpected failure must not stop the sweep from
                            // executing the rest of the due batch.
                            _logger.LogError(
                                ex,
                                "Scheduled account deletion threw for account {AccountId}.",
                                accountId);
                        }
                    }

                    if (executedCount > 0)
                    {
                        _logger.LogInformation(
                            "Account deletion sweep executed {Count} scheduled deletion(s).",
                            executedCount);
                    }
                },
                ct);
        }
        finally
        {
            await lockConnection.CloseAsync();
        }

        return executedCount;
    }
}
