using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Auth.Interfaces;

namespace PropertyApi.Infrastructure.Email;

/// <summary>
/// Queue + worker for security alert emails. See ISecurityAlertDispatcher for why the
/// previous fire-and-forget approach could not work.
///
/// The queue is bounded and drops the oldest entry when full. Alerts are advisory, and a
/// burst large enough to fill the channel is exactly when the request path must not start
/// blocking -- an unbounded queue would trade a dropped notification for unbounded memory
/// growth under a credential-stuffing run, which is a worse failure.
/// </summary>
public sealed class SecurityAlertBackgroundService : BackgroundService, ISecurityAlertDispatcher
{
    private const int QueueCapacity = 1_000;

    private readonly Channel<SecurityAlertRequest> _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SecurityAlertBackgroundService> _logger;

    private int _droppedAlerts;

    public SecurityAlertBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<SecurityAlertBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        _queue = Channel.CreateBounded<SecurityAlertRequest>(
            new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            },
            droppedAlert => OnDropped(droppedAlert));
    }

    public void Enqueue(SecurityAlertRequest alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        // TryWrite on a DropOldest channel only fails once the channel is completed,
        // i.e. during shutdown. Either way the caller is never blocked and never throws.
        if (!_queue.Writer.TryWrite(alert))
        {
            _logger.LogWarning(
                "Security alert {AlertKind} was not queued because the dispatcher is shutting down.",
                alert.Kind);
        }
    }

    private void OnDropped(SecurityAlertRequest alert)
    {
        var dropped = Interlocked.Increment(ref _droppedAlerts);

        _logger.LogWarning(
            "Security alert queue is full ({Capacity}); dropped the oldest {AlertKind} alert. " +
            "{DroppedTotal} alert(s) dropped so far this process.",
            QueueCapacity,
            alert.Kind,
            dropped);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Security alert dispatcher started.");

        try
        {
            await foreach (var alert in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await DeliverAsync(alert, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }

        _logger.LogInformation(
            "Security alert dispatcher stopped. Dropped alerts this process: {DroppedTotal}.",
            Volatile.Read(ref _droppedAlerts));
    }

    private async Task DeliverAsync(SecurityAlertRequest alert, CancellationToken ct)
    {
        try
        {
            // Its own scope, and therefore its own DbContext and mail client, with a
            // lifetime this worker controls. This is the whole reason the work moved off
            // the request thread.
            using var scope = _scopeFactory.CreateScope();
            var alerts = scope.ServiceProvider.GetRequiredService<ISecurityAlertService>();

            switch (alert.Kind)
            {
                case SecurityAlertKind.FailedLoginAttempt:
                    await alerts.SendFailedLoginAttemptAlertAsync(
                        alert.UserEmail,
                        alert.UserName,
                        alert.FailedAttemptCount,
                        alert.IpAddress,
                        alert.AttemptTimeUtc ?? DateTime.UtcNow,
                        ct);
                    break;

                case SecurityAlertKind.AccountLocked:
                    await alerts.SendAccountLockedAlertAsync(
                        alert.UserEmail,
                        alert.UserName,
                        alert.LockoutEnd,
                        alert.IpAddress,
                        ct);
                    break;

                case SecurityAlertKind.PasswordChangeRequest:
                    await alerts.SendPasswordChangeRequestAsync(
                        alert.UserEmail,
                        alert.UserName,
                        alert.PasswordResetLink ?? string.Empty,
                        alert.FailedAttemptCount,
                        alert.IpAddress,
                        ct);
                    break;

                case SecurityAlertKind.PasswordChangedConfirmation:
                    await alerts.SendPasswordChangedConfirmationAsync(
                        alert.UserEmail,
                        alert.UserName,
                        ct);
                    break;

                default:
                    _logger.LogError("Unhandled security alert kind {AlertKind}.", alert.Kind);
                    break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Every failure is logged. Previously these were unobserved task exceptions,
            // so a permanently broken mail path looked identical to a quiet one.
            _logger.LogError(
                ex,
                "Failed to deliver security alert {AlertKind}.",
                alert.Kind);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop accepting new alerts, then let ExecuteAsync drain what is already queued
        // before the host finishes shutting down.
        _queue.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }
}
