using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;

namespace HudhudNestApi.Infrastructure.SocialDistribution;

/// <summary>
/// Production <see cref="ISocialPublicationJobQueue"/> adapter (Phase 6 spec §15's "Infrastructure
/// Adapter قابل للربط مع Queue حقيقية"). Backed directly by the existing
/// <see cref="ISocialPublicationRepository"/> Queued/Retrying queries rather than a second,
/// independently-persisted job table — see the Port's own remarks for why. A future swap to a
/// real external broker (SQS/RabbitMQ/Azure Service Bus) only requires a new class implementing
/// this same interface plus one DI registration line — the Worker, the Distribution Engine, and
/// every command handler are untouched.
/// </summary>
public sealed class SocialPublicationJobQueue : ISocialPublicationJobQueue
{
    private readonly ISocialPublicationRepository _publications;
    private readonly ILogger<SocialPublicationJobQueue> _logger;

    public SocialPublicationJobQueue(ISocialPublicationRepository publications, ILogger<SocialPublicationJobQueue> logger)
    {
        _publications = publications;
        _logger = logger;
    }

    public Task EnqueueAsync(Guid publicationId, CancellationToken ct = default)
    {
        _logger.LogDebug("Social publication {PublicationId} is now queued for dispatch.", publicationId);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<SocialPublicationJob>> DequeueDueBatchAsync(int take, DateTime utcNow, CancellationToken ct = default)
    {
        var due = (await _publications.GetDueToPublishAsync(utcNow, take, ct))
            .Concat(await _publications.GetDueForRetryAsync(utcNow, take, ct));

        return due
            // Content is always attached once a publication reaches Queued/Retrying (Queue()
            // requires it) — see SocialPublication.Queue.
            .Where(p => p.Content is not null)
            .Select(p => new SocialPublicationJob(
                JobId: p.Id,
                PublicationId: p.Id,
                SocialAccountId: p.SocialAccountId,
                Platform: p.Content!.Platform,
                IdempotencyKey: $"social-publication:{p.Id}:v{p.Content.ContentVersion}",
                Attempt: p.RetryCount,
                CreatedAt: p.CreatedAt))
            .ToList();
    }
}
