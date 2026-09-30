using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Commands.PublishSocialPublication;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;
using HudhudNestApi.Application.SocialDistribution.Options;
using HudhudNestApi.Application.SocialDistribution.Services;
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

                    // Before anything else: recover any publication a crashed/restarted worker
                    // left stuck in Publishing past its lease (Phase 1 audit F-8) — must run
                    // before dequeuing so a reaped row's dead letter is recorded up front, not
                    // racing this same sweep's own dispatch pass.
                    await ReapExpiredLeasesAsync(scope.ServiceProvider, uow, deadLetters, ct);

                    // Before dequeuing, so a listing the sweep just distributed is dispatched in
                    // this same pass instead of waiting another interval.
                    await ReconcileAsync(scope.ServiceProvider, ct);

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

    /// <summary>
    /// Safety net behind <c>PropertyPublishedEvent</c> (see SocialDistributionReconciliationOptions
    /// for why it only looks back a few days): distributes any recently published listing that no
    /// <c>DistributionRun</c> has ever evaluated. Each listing runs in its own scope so one
    /// failure (or a half-tracked EF state after it) can never poison the next listing or the
    /// dispatch pass that follows — and a failure here never blocks publishing queued work.
    /// </summary>
    private async Task ReconcileAsync(IServiceProvider services, CancellationToken ct)
    {
        var options = services.GetService<IOptions<SocialDistributionReconciliationOptions>>()?.Value
            ?? new SocialDistributionReconciliationOptions();

        if (!options.Enabled)
            return;

        IReadOnlyList<Guid> propertyIds;
        try
        {
            var now = DateTime.UtcNow;
            propertyIds = await services.GetRequiredService<IDistributionRunRepository>()
                .GetPublishedPropertyIdsWithoutRunAsync(
                    publishedSinceUtc: now.AddDays(-Math.Max(options.LookbackDays, 1)),
                    publishedBeforeUtc: now - options.MinAge,
                    take: options.BatchSize,
                    ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Social distribution reconciliation could not list candidate listings.");
            return;
        }

        foreach (var propertyId in propertyIds)
        {
            try
            {
                using var propertyScope = _scopeFactory.CreateScope();
                var engine = propertyScope.ServiceProvider.GetRequiredService<IDistributionEngine>();
                var run = await engine.RunAsync(propertyId, DistributionRunTriggerType.Reconciliation, triggeredByUserId: null, ct);

                _logger.LogInformation(
                    "Reconciliation distributed listing {PropertyId}: {MatchedRuleCount} matching rule(s), {PublicationsCreatedCount} publication(s) created, {SkippedCount} skipped.",
                    propertyId, run.MatchedRuleCount, run.PublicationsCreatedCount, run.SkippedCount);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Reconciliation failed to distribute listing {PropertyId}.", propertyId);
            }
        }
    }

    /// <summary>
    /// Recovers every publication stuck in Publishing past its lease (Phase 1 audit F-8) — always
    /// terminal (never re-queued: see <see cref="SocialPublication.ReleaseExpiredLease"/> for why
    /// auto-retrying an unconfirmed outcome is never safe here), and always recorded as a dead
    /// letter so an admin is the one who decides whether the platform actually received the post.
    /// </summary>
    private async Task ReapExpiredLeasesAsync(
        IServiceProvider services, IUnitOfWork uow, ISocialPublicationDeadLetterRepository deadLetters, CancellationToken ct)
    {
        var publications = services.GetRequiredService<ISocialPublicationRepository>();
        var now = DateTime.UtcNow;

        IReadOnlyList<SocialPublication> stuck;
        try
        {
            stuck = await publications.GetPublishingWithExpiredLeaseAsync(now, MaxItemsPerSweep, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Social distribution lease reaper could not list expired-lease publications.");
            return;
        }

        foreach (var publication in stuck)
        {
            try
            {
                var fromStatus = publication.Status;
                publication.ReleaseExpiredLease(now);

                if (publication.Status == fromStatus)
                    continue; // lease had not actually expired by the time this row was read — leave it alone.

                publications.Update(publication);

                var history = services.GetRequiredService<ISocialPublicationStatusHistoryRepository>();
                await history.AddAsync(
                    SocialPublicationStatusHistory.Record(
                        publication.Id, fromStatus, publication.Status, changedByUserId: null,
                        "تم تحرير قفل النشر بعد انتهاء مهلته — يُشتبه بتعطل العامل الخلفي أثناء محاولة سابقة.", now),
                    ct);
                await uow.SaveChangesAsync(ct);

                _logger.LogWarning(
                    "Released expired publish lease for SocialPublication {PublicationId} — recorded as {ErrorCode}.",
                    publication.Id, publication.ErrorCode);

                await RecordDeadLetterIfNeededAsync(
                    deadLetters, uow, publication.Content!.Platform,
                    SocialDistributionMapper.ToDto(publication), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to release expired publish lease for SocialPublication {PublicationId}.", publication.Id);
            }
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
