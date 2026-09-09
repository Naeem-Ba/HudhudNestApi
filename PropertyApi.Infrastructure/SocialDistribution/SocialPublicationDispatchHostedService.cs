using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.SocialDistribution.Commands.PublishSocialPublication;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Infrastructure.Persistence;

namespace PropertyApi.Infrastructure.SocialDistribution;

/// <summary>
/// Polls for Queued (due) and Retrying (due-for-retry) publications and executes them through
/// the exact same <see cref="PublishSocialPublicationCommand"/> the manual "publish now" admin
/// endpoint uses (spec §14: this IS the Job — see PublishSocialPublicationCommandHandler for why
/// there is deliberately no separate worker-only execution path). Pattern mirrors
/// ListingExpiryHostedService/SavedSearchMatchHostedService exactly: IServiceScopeFactory + a
/// polling loop + a Postgres advisory lock, because this codebase has no real queue/worker
/// infrastructure (Hangfire/Quartz/...) — see docs for what a real Job/Queue system would still
/// need to add on top (leases, cross-instance exactly-once, backpressure).
/// </summary>
public sealed class SocialPublicationDispatchHostedService : BackgroundService
{
    /// <summary>
    /// Frequent enough that a "publish ASAP" publication does not sit idle for long, without
    /// hammering the database — this codebase's other sweeps run every few hours because their
    /// work is day-granularity; distribution posting is closer to real-time, hence the shorter
    /// interval here.
    /// </summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(2);

    /// <summary>Caps one sweep's blast radius — a large backlog drains over several sweeps rather than one huge run.</summary>
    private const int MaxItemsPerSweep = 25;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SocialPublicationDispatchHostedService> _logger;

    public SocialPublicationDispatchHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<SocialPublicationDispatchHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Social distribution dispatch worker started. Interval={Interval}.", SweepInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Social distribution dispatch sweep failed.");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }

    /// <summary>One full sweep. Internal so it can be driven directly from a test without waiting on the timer.</summary>
    internal async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await BackgroundJobLock.TryRunAsync(
                db.Database.GetDbConnection(),
                BackgroundJobLockKeys.SocialPublicationDispatch,
                nameof(SocialPublicationDispatchHostedService),
                _logger,
                async () =>
                {
                    var publications = scope.ServiceProvider.GetRequiredService<ISocialPublicationRepository>();
                    var mediator = scope.ServiceProvider.GetRequiredService<ISender>();
                    var now = DateTime.UtcNow;

                    var candidates = (await publications.GetDueToPublishAsync(now, MaxItemsPerSweep, ct))
                        .Concat(await publications.GetDueForRetryAsync(now, MaxItemsPerSweep, ct))
                        .ToList();

                    var processed = 0;
                    foreach (var publication in candidates)
                    {
                        try
                        {
                            // Each Send runs its own StartPublishing→...→SaveChanges cycle
                            // (PublishSocialPublicationCommandHandler) — one candidate failing
                            // must never abort the rest of the sweep.
                            await mediator.Send(new PublishSocialPublicationCommand(publication.Id), ct);
                            processed++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to dispatch SocialPublication {PublicationId}.", publication.Id);
                        }
                    }

                    if (processed > 0)
                    {
                        _logger.LogInformation(
                            "Social distribution dispatch sweep processed {Count} publication(s).",
                            processed);
                    }
                },
                ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
