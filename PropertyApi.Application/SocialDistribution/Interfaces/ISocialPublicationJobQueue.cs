using PropertyApi.Application.SocialDistribution.DTOs;

namespace PropertyApi.Application.SocialDistribution.Interfaces;

/// <summary>
/// The Queue Port (Phase 6 spec §15: "أنشئ Queue Port... أنشئ InMemoryQueueAdapter للاختبارات...
/// أنشئ Infrastructure Adapter قابلاً للربط مع Queue حقيقية"). <c>SocialPublicationDispatchHostedService</c>
/// (the Worker) depends only on this interface, never on how "due work" is actually stored or
/// delivered — swapping the production DB-backed adapter for a real message-broker-backed one
/// later requires no change to the Worker, the Distribution Engine, or Domain.
///
/// One <see cref="SocialPublicationJob"/> per <c>SocialPublication</c> — never one job covering
/// several platforms/accounts (spec §10: "يجب أن يكون لكل SocialPublication Job مستقل").
/// </summary>
public interface ISocialPublicationJobQueue
{
    /// <summary>
    /// Signals that <paramref name="publicationId"/> is now due for processing. The production
    /// adapter is a documented no-op beyond logging — the durable state change IS
    /// <c>SocialPublication.Queue()</c>/<c>RetryManually()</c> already having been saved by the
    /// caller; this call exists so every caller depends on the Port (not on that detail), which is
    /// what makes swapping in a real external queue later a one-file change.
    /// </summary>
    Task EnqueueAsync(Guid publicationId, CancellationToken ct = default);

    /// <summary>Due Queued/Retrying publications, oldest-due first, capped at <paramref name="take"/> — what the Worker consumes each sweep.</summary>
    Task<IReadOnlyList<SocialPublicationJob>> DequeueDueBatchAsync(int take, DateTime utcNow, CancellationToken ct = default);
}
