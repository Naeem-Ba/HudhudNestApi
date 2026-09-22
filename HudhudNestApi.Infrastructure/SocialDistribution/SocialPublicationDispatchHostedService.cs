using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Commands.PublishSocialPublication;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Infrastructure.Persistence;

namespace HudhudNestApi.Infrastructure.SocialDistribution;

/// <summary>
/// Polls the <see cref="ISocialPublicationJobQueue"/> Port for due jobs and executes each through
/// the exact same <see cref="PublishSocialPublicationCommand"/> the manual "publish now" admin
/// endpoint uses (spec Phase 3 §14 / Phase 6 §15: this IS the Worker — see
/// PublishSocialPublicationCommandHandler for why there is deliberately no separate worker-only
/// execution path). Pattern mirrors ListingExpiryHostedService/SavedSearchMatchHostedService
/// exactly: IServiceScopeFactory + a polling loop + a Postgres advisory lock, because this
/// codebase has no real queue/broker infrastructure (Hangfire/Quartz/SQS/...) — see docs for what
/// a real Job/Queue system would still need to add on top (leases, cross-instance exactly-once,
/// backpressure).
///
/// Phase 6 addition: after each job, a publication that landed in the TERMINAL
/// <see cref="SocialPublicationStatus.Failed"/> state (never <c>Retrying</c> — that one will be
/// picked up again automatically) is recorded as a <see cref="SocialPublicationDeadLetter"/> if
/// one does not already exist for it — the durable, admin-visible failure ledger (spec §13).
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
                    var jobQueue = scope.ServiceProvider.GetRequiredService<ISocialPublicationJobQueue>();
                    var deadLetters = scope.ServiceProvider.GetRequiredService<ISocialPublicationDeadLetterRepository>();
                    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var mediator = scope.ServiceProvider.GetRequiredService<ISender>();
                    var now = DateTime.UtcNow;

                    var jobs = await jobQueue.DequeueDueBatchAsync(MaxItemsPerSweep, now, ct);

                    var processed = 0;
                    foreach (var job in jobs)
                    {
                        try
                        {
                            // Each Send runs its own StartPublishing→...→SaveChanges cycle
                            // (PublishSocialPublicationCommandHandler) — one candidate failing
                            // must never abort the rest of the sweep, and Facebook/Instagram/
                            // Telegram jobs for the same property are fully independent of one
                            // another's outcome.
                            var result = await mediator.Send(new PublishSocialPublicationCommand(job.PublicationId), ct);
                            processed++;

                            if (result.Status == SocialPublicationStatus.Failed)
                                await RecordDeadLetterIfNeededAsync(deadLetters, uow, job.Platform, result, ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to dispatch SocialPublication {PublicationId}.", job.PublicationId);
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

    private async Task RecordDeadLetterIfNeededAsync(
        ISocialPublicationDeadLetterRepository deadLetters,
        IUnitOfWork uow,
        SocialPlatform platform,
        Application.SocialDistribution.DTOs.SocialPublicationDto publication,
        CancellationToken ct)
    {
        if (await deadLetters.ExistsUnresolvedForPublicationAsync(publication.Id, ct))
            return;

        var deadLetter = SocialPublicationDeadLetter.Create(
            publication.Id,
            publication.SocialAccountId,
            platform,
            publication.ErrorCode ?? SocialPublicationErrorCode.NetworkError,
            publication.ErrorMessage ?? "فشل غير معروف.",
            publication.RetryCount,
            publication.FailedAt ?? DateTime.UtcNow);

        await deadLetters.AddAsync(deadLetter, ct);
        await uow.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Social publication {PublicationId} moved to dead letter after exhausting its retry budget (ErrorCode={ErrorCode}).",
            publication.Id,
            deadLetter.LastErrorCode);
    }
}
