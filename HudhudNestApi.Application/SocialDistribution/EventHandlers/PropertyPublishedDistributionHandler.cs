using MediatR;
using Microsoft.Extensions.Logging;
using HudhudNestApi.Application.Listings.Events;
using HudhudNestApi.Application.SocialDistribution.Services;
using HudhudNestApi.Domain.SocialDistribution.Enums;

namespace HudhudNestApi.Application.SocialDistribution.EventHandlers;

/// <summary>
/// SocialDistribution's side of the cross-context flow (Phase 4 spec §9): listens for
/// <see cref="PropertyPublishedEvent"/> (raised by Listings — see that type's remarks) and runs
/// the distribution engine automatically. Listings never references this class, this namespace,
/// or SocialDistribution at all — MediatR's notification dispatch is what wires the two contexts
/// together, and only from this side.
///
/// Every exception is caught and logged here, never rethrown: a broken rule/account/provider must
/// never turn into a failed property publish (spec §9: "لا تجعل نشر العقار نفسه يفشل إلا إذا
/// كانت هناك سياسة صريحة بذلك" — no such policy exists in this design). This is also what makes
/// the integration point idempotent from Listings' perspective — Listings' own guard
/// (PublishPropertyCommandHandler only calls Property.Publish()/raises this event on an actual
/// Unpublished → Published transition) is what prevents this handler from ever being invoked
/// twice for the same publish; DistributionEngine's own per-account duplicate check
/// (ISocialPublicationRepository.ExistsActiveForPropertyAndAccountAsync) is the defense-in-depth
/// layer beneath that.
/// </summary>
public sealed class PropertyPublishedDistributionHandler : INotificationHandler<PropertyPublishedEvent>
{
    private readonly IDistributionEngine _engine;
    private readonly ILogger<PropertyPublishedDistributionHandler> _logger;

    public PropertyPublishedDistributionHandler(IDistributionEngine engine, ILogger<PropertyPublishedDistributionHandler> logger)
    {
        _engine = engine;
        _logger = logger;
    }

    public async Task Handle(PropertyPublishedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            var run = await _engine.RunAsync(
                notification.PropertyId,
                DistributionRunTriggerType.PropertyPublished,
                notification.PublishedByUserId,
                cancellationToken);

            _logger.LogInformation(
                "اكتمل التوزيع الآلي للعقار {PropertyId}: {MatchedRuleCount} قاعدة مطابقة، {PublicationsCreatedCount} منشور تم إنشاؤه، {SkippedCount} تم تخطيه.",
                notification.PropertyId, run.MatchedRuleCount, run.PublicationsCreatedCount, run.SkippedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "فشل التوزيع الآلي للعقار {PropertyId} بعد نشره. لن يؤثر ذلك على حالة نشر العقار نفسه.",
                notification.PropertyId);
        }
    }
}
