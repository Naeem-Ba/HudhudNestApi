using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using PropertyApi.Application.Valuation.Interfaces;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.Valuation;

/// <summary>
/// Stage 5 — enforces ValuationInquiry's 24h SLA. Pattern mirrors
/// AccountDeletionSweepHostedService/AuditLogRetentionHostedService exactly (the newest
/// hosted-service shape in this codebase): IServiceScopeFactory + TimeProvider-driven
/// PeriodicTimer, guarded by a Postgres advisory lock
/// (BackgroundJobLockKeys.ValuationInquiryExpiry) so of several deployed instances running the
/// same timer, only one actually executes a given sweep.
///
/// All the actual decision logic (which rows qualify, what transitions happen, which
/// notifications fire) lives in IValuationSlaEnforcementService — an Application-layer service
/// resolved fresh from this sweep's own scope, unit-testable with Moq the same way
/// OfficeMatchingService already is. This class is deliberately thin: timer + lock + one
/// resolved-service call + logging.
/// </summary>
public sealed class ValuationInquiryExpiryHostedService : BackgroundService
{
    /// <summary>
    /// The SLA is measured in hours (24h), not days, so this needs to run noticeably more
    /// often than ListingExpiryHostedService's 6-hour listing sweep — otherwise an inquiry
    /// could sit expired-but-unnoticed for a large fraction of its own window before anyone is
    /// told.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Cap per phase per sweep — same backlog-draining reasoning as
    /// ListingExpiryHostedService.MaxItemsPerPhase.
    /// </summary>
    private const int MaxItemsPerPhase = 200;

    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _clock;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ValuationInquiryExpiryHostedService> _logger;

    public ValuationInquiryExpiryHostedService(
        IServiceScopeFactory scopes,
        TimeProvider clock,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<ValuationInquiryExpiryHostedService> logger)
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
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Valuation inquiry expiry sweep failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// One sweep. Internal so a test can drive it directly instead of waiting on the timer —
    /// same convention AccountDeletionSweepHostedService.SweepAsync/
    /// ListingExpiryHostedService.RunOnceAsync already use.
    /// </summary>
    internal async Task SweepAsync(CancellationToken ct)
    {
        var connectionString = PostgresConnectionStringResolver.Resolve(_configuration, _environment);
        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(ct);

        try
        {
            await BackgroundJobLock.TryRunAsync(
                lockConnection,
                BackgroundJobLockKeys.ValuationInquiryExpiry,
                nameof(ValuationInquiryExpiryHostedService),
                _logger,
                async () =>
                {
                    var now = _clock.GetUtcNow().UtcDateTime;

                    using var scope = _scopes.CreateScope();
                    var slaEnforcement = scope.ServiceProvider.GetRequiredService<IValuationSlaEnforcementService>();

                    var result = await slaEnforcement.RunSweepAsync(now, MaxItemsPerPhase, ct);

                    if (result.InquiriesExpired + result.InvitationsExpired + result.RemindersSent + result.NotificationsRetried > 0)
                    {
                        _logger.LogInformation(
                            "Valuation inquiry expiry sweep complete. InquiriesExpired={InquiriesExpired}, " +
                            "InvitationsExpired={InvitationsExpired}, RemindersSent={RemindersSent}, " +
                            "NotificationsRetried={NotificationsRetried}.",
                            result.InquiriesExpired,
                            result.InvitationsExpired,
                            result.RemindersSent,
                            result.NotificationsRetried);
                    }
                },
                ct);
        }
        finally
        {
            await lockConnection.CloseAsync();
        }
    }
}
