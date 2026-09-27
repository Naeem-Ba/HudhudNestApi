using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Events;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Domain.SocialDistribution.Entities;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;
using PropertyApi.Domain.SocialDistribution.Policies;

namespace PropertyApi.Application.SocialDistribution.EventHandlers;

/// <summary>
/// SocialDistribution's side of Phase 11 (spec §7): listens for <see cref="PropertyStatusChangedEvent"/>
/// (raised by Listings — see that type's remarks) and, for every currently-live publication of
/// this property, resolves what should happen via <see cref="SocialPublicationLifecyclePolicy"/>
/// and executes it ONLY if the target platform's <see cref="SocialPublisherCapabilities"/> actually
/// support that operation (spec: "لا تفترض أن كل منصة تدعم نفس العمليات") — never assumed, always
/// checked, and silently downgraded to a logged no-op when unsupported rather than attempted.
///
/// Same isolation/failure posture as <see cref="PropertyPublishedDistributionHandler"/>: every
/// exception is caught and logged here, never rethrown — a broken publisher/registry entry must
/// never turn into a failed property status update.
/// </summary>
public sealed class PropertyStatusChangedDistributionHandler :
    INotificationHandler<PropertyStatusChangedEvent>, INotificationHandler<PropertyDeletedEvent>
{
    private readonly ISocialPublicationRepository _publications;
    private readonly ISocialAccountRepository _accounts;
    private readonly ISocialPublicationStatusHistoryRepository _history;
    private readonly ISocialPublisherRegistry _registry;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<PropertyStatusChangedDistributionHandler> _logger;

    public PropertyStatusChangedDistributionHandler(
        ISocialPublicationRepository publications,
        ISocialAccountRepository accounts,
        ISocialPublicationStatusHistoryRepository history,
        ISocialPublisherRegistry registry,
        IUnitOfWork uow,
        ILogger<PropertyStatusChangedDistributionHandler> logger)
    {
        _publications = publications;
        _accounts = accounts;
        _history = history;
        _registry = registry;
        _uow = uow;
        _logger = logger;
    }

    public async Task Handle(PropertyStatusChangedEvent notification, CancellationToken ct)
    {
        var action = SocialPublicationLifecyclePolicy.Resolve(notification.PreviousStatus, notification.NewStatus);
        if (action == SocialPublicationLifecycleAction.NoOp)
            return; // Nothing to evaluate at all — don't even load the publications.

        try
        {
            var transitionTag = $"PropertyStatusChanged:{notification.PreviousStatus}->{notification.NewStatus}";
            var publications = await _publications.GetActiveForPropertyAsync(notification.PropertyId, ct);

            foreach (var publication in publications)
                await ApplyToOnePublicationAsync(publication, action, transitionTag, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "فشلت معالجة تغيّر حالة العقار {PropertyId} ({Previous} → {New}) على مستوى التوزيع الاجتماعي. لن يؤثر ذلك على تحديث حالة العقار نفسه.",
                notification.PropertyId, notification.PreviousStatus, notification.NewStatus);
        }
    }

    /// <summary>
    /// Phase 1 audit F-10: a deleted listing never went through a Status transition
    /// <see cref="SocialPublicationLifecyclePolicy"/> could key off (see
    /// <see cref="PropertyDeletedEvent"/>'s own remarks for why) — this always means Delete,
    /// unconditionally, for every currently-live publication of the property.
    /// </summary>
    public async Task Handle(PropertyDeletedEvent notification, CancellationToken ct)
    {
        try
        {
            var transitionTag = $"PropertyDeleted:{notification.PropertyId}";
            var publications = await _publications.GetActiveForPropertyAsync(notification.PropertyId, ct);

            foreach (var publication in publications)
                await ApplyToOnePublicationAsync(publication, SocialPublicationLifecycleAction.Delete, transitionTag, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "فشلت معالجة حذف العقار {PropertyId} على مستوى التوزيع الاجتماعي. لن يؤثر ذلك على حذف العقار نفسه.",
                notification.PropertyId);
        }
    }

    private async Task ApplyToOnePublicationAsync(
        SocialPublication publication, SocialPublicationLifecycleAction action, string transitionTag, CancellationToken ct)
    {
        // Idempotency (spec §7): never repeat the same transition's action for the same
        // publication — e.g. UpdatePropertyCommandHandler somehow being invoked twice for the
        // same actual before/after status pair must not double-comment or attempt a second delete.
        var existingHistory = await _history.GetByPublicationIdAsync(publication.Id, ct);
        if (existingHistory.Any(h => h.Note is not null && h.Note.Contains(transitionTag, StringComparison.Ordinal)))
        {
            _logger.LogInformation("تم تجاهل إجراء دورة حياة مكرر ({Tag}) للمنشور {PublicationId}.", transitionTag, publication.Id);
            return;
        }

        if (publication.Content is null || string.IsNullOrEmpty(publication.ExternalPostId))
        {
            await RecordAsync(publication, $"{transitionTag}: تم التجاهل — لا يوجد محتوى/معرّف منشور خارجي.", ct);
            return;
        }

        var account = await _accounts.GetByIdAsync(publication.SocialAccountId, ct);
        var publisher = account is null ? null : _registry.TryGetPublisher(account.Platform);
        if (publisher is null)
        {
            await RecordAsync(publication, $"{transitionTag}: تم التجاهل — لا يوجد Publisher مسجَّل لهذه المنصة.", ct);
            return;
        }

        var capabilities = publisher.GetCapabilities();
        var (supported, effectiveAction) = action switch
        {
            SocialPublicationLifecycleAction.Update => (capabilities.SupportsUpdate, action),
            SocialPublicationLifecycleAction.Comment => (capabilities.SupportsComment, action),
            SocialPublicationLifecycleAction.Delete => (capabilities.SupportsDelete, action),
            _ => (false, SocialPublicationLifecycleAction.NoOp),
        };

        if (!supported)
        {
            await RecordAsync(publication, $"{transitionTag}: تم التجاهل (No-op) — منصة {account!.Platform} لا تدعم {effectiveAction}.", ct);
            return;
        }

        var result = effectiveAction switch
        {
            SocialPublicationLifecycleAction.Update => await publisher.UpdateAsync(
                BuildUpdateRequest(publication, account!), publication.ExternalPostId!, ct),
            SocialPublicationLifecycleAction.Comment => await publisher.CommentAsync(
                publication.ExternalPostId!, BuildStatusComment(publication), ct),
            SocialPublicationLifecycleAction.Delete => await publisher.DeleteAsync(publication.ExternalPostId!, ct),
            _ => SocialPublishResult.Failure(SocialPublicationErrorCode.PlatformNotConfigured, "no-op"),
        };

        // Never claim success the provider didn't confirm (spec rule 15) — the outcome is
        // recorded exactly as the (placeholder, today) publisher reported it.
        var outcome = result.IsSuccess ? "نجح" : $"فشل ({result.ErrorCode}: {result.ErrorMessage})";
        await RecordAsync(publication, $"{transitionTag}: تم تنفيذ {effectiveAction} — {outcome}.", ct);
    }

    private static SocialPublishRequest BuildUpdateRequest(SocialPublication publication, SocialAccount account) => new()
    {
        PublicationId = publication.Id,
        Platform = account.Platform,
        ExternalAccountId = account.ExternalAccountId,
        CredentialReference = account.CredentialReference,
        Title = publication.Content!.Title,
        Body = BuildStatusComment(publication) + "\n" + publication.Content.Body,
        ImageUrl = publication.Content.ImageUrl,
        TargetUrl = publication.Content.TargetUrl,
        Hashtags = publication.Content.HashtagList,
        Language = publication.Content.Language,
    };

    private static string BuildStatusComment(SocialPublication publication) =>
        publication.Content!.Language == "en" ? "This listing's status has changed." : "تم تحديث حالة هذا العقار.";

    private async Task RecordAsync(SocialPublication publication, string note, CancellationToken ct)
    {
        await _history.AddAsync(
            SocialPublicationStatusHistory.Record(publication.Id, publication.Status, publication.Status, changedByUserId: null, note, DateTime.UtcNow),
            ct);
        await _uow.SaveChangesAsync(ct);
    }
}
