using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.Services;

/// <summary>
/// Orchestrates the Phase 4 flow end to end (spec §8/§27): load active rules, evaluate them
/// against a property, resolve priority/specificity conflicts, remove duplicates, create one
/// SocialPublication (+content, +UTM target URL) per winning account, and queue each one — all
/// inside a single <c>DistributionRun</c>. Deliberately an Application-layer interface/service
/// (not Infrastructure): every dependency it needs (repositories, <c>ISender</c>, the target-URL
/// builder) is already an abstraction, so no ORM/HTTP/queue concrete type ever leaks into it.
/// </summary>
public interface IDistributionEngine
{
    /// <summary>
    /// Runs a full distribution pass for <paramref name="propertyId"/> and persists its result as
    /// a new <c>DistributionRun</c>. Never throws for an ordinary "nothing to do" outcome (no
    /// matching rules, every matched account ineligible) — those are recorded on the run itself.
    /// Only throws for a genuine precondition failure (property missing/not public), which the
    /// caller (either <c>DispatchPropertyDistributionCommandHandler</c> for a manual admin call,
    /// or <c>PropertyPublishedDistributionHandler</c> for the automatic trigger, which swallows
    /// it) decides how to surface.
    /// </summary>
    Task<DistributionRunDto> RunAsync(
        Guid propertyId,
        DistributionRunTriggerType triggerType,
        Guid? triggeredByUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Read-only equivalent of <see cref="RunAsync"/> (spec §18: "معاينة القواعد المطابقة لعقار
    /// معين"/"POST /evaluate/{propertyId}") — evaluates the same rules the same way, but never
    /// creates a DistributionRun or a SocialPublication.
    /// </summary>
    Task<DistributionPreviewDto> PreviewAsync(Guid propertyId, CancellationToken ct = default);
}
