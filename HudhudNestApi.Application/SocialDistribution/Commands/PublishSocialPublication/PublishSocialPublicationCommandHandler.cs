using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.Listings.Interfaces;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;
using HudhudNestApi.Application.SocialDistribution.Options;
using HudhudNestApi.Application.SocialDistribution.Services;
using HudhudNestApi.Domain.SocialDistribution.Entities;
using HudhudNestApi.Domain.SocialDistribution.Enums;
using HudhudNestApi.Domain.SocialDistribution.Models;
using HudhudNestApi.Domain.SocialDistribution.Policies;

namespace HudhudNestApi.Application.SocialDistribution.Commands.PublishSocialPublication;

/// <summary>
/// The heart of the distribution engine — and the ONLY place that ever generates a branded social
/// media asset or calls an <see cref="ISocialPublisher"/> (spec Phase 6 rule 8: "لا تنفذ Social
/// API داخل HTTP Request"). For an automatically-distributed publication
/// (<see cref="SocialPublication.DistributionRuleId"/> not null) this handler is invoked ONLY by
/// <c>SocialPublicationDispatchHostedService</c> — never synchronously from the property-publish
/// HTTP request, since <c>DistributionEngine</c> only ever sends
/// <c>CreateSocialPublicationCommand</c>/<c>QueueSocialPublicationCommand</c> (pure DB writes),
/// never this command. The manual "publish now" admin endpoint calls this same handler
/// synchronously, which is an explicit, deliberate admin action (spec allows this — see Phase 3
/// docs) — not the automatic path this rule targets.
///
/// Re-validates the property/account are still eligible AT THE MOMENT OF EXECUTION (not just when
/// queued — spec §13: an account/property can become ineligible while a publication is waiting),
/// then (for rule-engine-created publications only) ensures a template-generated social media
/// asset is attached, validates the resulting
/// content against the target platform's real capabilities, and only then delegates the actual
/// platform call to whichever <see cref="ISocialPublisher"/> the <see cref="ISocialPublisherRegistry"/>
/// resolves for the account's platform.
///
/// Concurrency: <see cref="SocialPublication.StartPublishing"/> only accepts Queued/Retrying,
/// so a second concurrent call against the same row either loses the race on SaveChanges
/// (xmin optimistic-concurrency token — see SocialPublicationConfiguration) or, if it reads
/// after the first already committed, fails EnsureStatus immediately. The dispatch worker
/// additionally never selects the same row twice in the same sweep (see
/// ISocialPublicationJobQueue.DequeueDueBatchAsync + its Postgres advisory lock), so
/// double-execution across separate worker instances is the one scenario a real distributed
/// lease/lock would still need to close out further — documented as a limitation.
/// </summary>
public sealed class PublishSocialPublicationCommandHandler
    : IRequestHandler<PublishSocialPublicationCommand, SocialPublicationDto>
{
    private readonly ISocialPublicationRepository _publications;
    private readonly ISocialPublicationStatusHistoryRepository _history;
    private readonly ISocialAccountRepository _accounts;
    private readonly IPropertyRepository _properties;
    private readonly ISocialPublisherRegistry _publisherRegistry;
    private readonly ISocialMediaAssetGenerator _assetGenerator;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<PublishSocialPublicationCommandHandler> _logger;
    private readonly SocialDistributionRetryOptions _retryOptions;

    public PublishSocialPublicationCommandHandler(
        ISocialPublicationRepository publications,
        ISocialPublicationStatusHistoryRepository history,
        ISocialAccountRepository accounts,
        IPropertyRepository properties,
        ISocialPublisherRegistry publisherRegistry,
        ISocialMediaAssetGenerator assetGenerator,
        IUnitOfWork uow,
        ILogger<PublishSocialPublicationCommandHandler> logger,
        IOptions<SocialDistributionRetryOptions>? retryOptions = null)
    {
        _publications = publications;
        _history = history;
        _accounts = accounts;
        _properties = properties;
        _publisherRegistry = publisherRegistry;
        _assetGenerator = assetGenerator;
        _uow = uow;
        _logger = logger;
        // Optional so every existing test double (Mock.Of<IOptions<...>>, or simply omitting the
        // argument) keeps working unchanged — falls back to SocialPublicationRetryPolicy's own
        // hardcoded defaults exactly like before this option existed.
        _retryOptions = retryOptions?.Value ?? new SocialDistributionRetryOptions();
    }

    public async Task<SocialPublicationDto> Handle(PublishSocialPublicationCommand request, CancellationToken ct)
    {
        var publication = await _publications.GetByIdAsync(request.PublicationId, ct)
            ?? throw new NotFoundException("منشور التوزيع غير موجود.");

        if (publication.Content is null)
            throw new ConflictException("لا يمكن نشر منشور بلا محتوى.");

        var now = DateTime.UtcNow;
        var fromStatus = publication.Status;

        // Throws InvalidStateTransitionException (-> 409) if not Queued/Retrying — this IS the
        // idempotency guard: a second concurrent call against an already-Publishing/Published
        // row is rejected here, before any external call is made.
        publication.StartPublishing(now);
        _publications.Update(publication);

        var account = await _accounts.GetByIdAsync(publication.SocialAccountId, ct);
        var propertyStillPublic = await _properties.IsPubliclyVisibleAsync(publication.PropertyId, ct);

        string? note;

        if (!propertyStillPublic)
        {
            FailNonRetryable(publication, SocialPublicationErrorCode.PropertyNotPublic, "العقار لم يعد متاحاً للعامة وقت النشر.", now);
            note = publication.ErrorMessage;
        }
        else if (account is null || !account.CanPublish())
        {
            FailNonRetryable(publication, SocialPublicationErrorCode.PermissionDenied, "الحساب الاجتماعي المستهدف لم يعد فعّالاً وقت النشر.", now);
            note = publication.ErrorMessage;
        }
        else
        {
            var publisher = _publisherRegistry.TryGetPublisher(account.Platform);

            SocialPublishResult result;
            if (publisher is null)
            {
                result = SocialPublishResult.Failure(
                    SocialPublicationErrorCode.PlatformNotConfigured,
                    "لا يوجد Publisher مسجل لهذه المنصة.");
            }
            else
            {
                // Phase 7: only rule-engine-created (automatic) publications get a branded
                // template asset generated for them — a manually-created publication keeps
                // whatever ImageUrl its caller explicitly supplied (spec: never override an
                // admin's own choice of image). A publication whose content already carries a
                // generated asset (e.g. a retry after a transient publish failure) reuses it —
                // asset generation never re-runs for an already-valid asset.
                var assetOutcome = publication.DistributionRuleId is not null && publication.Content.SocialMediaAssetId is null
                    ? await TryAttachGeneratedAssetAsync(publication, ct)
                    : AssetOutcome.NotNeeded;

                if (assetOutcome.Failed)
                {
                    result = SocialPublishResult.Failure(assetOutcome.ErrorCode!.Value, assetOutcome.ErrorMessage!);
                }
                else
                {
                    var publishRequest = BuildPublishRequest(publication, account);
                    var validation = publisher.ValidateContent(publishRequest);

                    if (!validation.IsValid)
                    {
                        result = SocialPublishResult.Failure(
                            SocialPublicationErrorCode.InvalidContent,
                            string.Join(" | ", validation.Errors));
                    }
                    else
                    {
                        try
                        {
                            result = await publisher.PublishAsync(publishRequest, ct);
                        }
                        catch (Exception ex)
                        {
                            // A publisher must not throw for an ordinary platform failure (see
                            // ISocialPublisher remarks) — an exception escaping it is unexpected, so
                            // it is logged in full here but the publication itself only ever stores
                            // the sanitized, generic message below, never the raw exception (spec §22).
                            _logger.LogError(
                                ex,
                                "Unexpected exception from ISocialPublisher for publication {PublicationId} / platform {Platform}.",
                                publication.Id,
                                account.Platform);

                            result = SocialPublishResult.Failure(
                                SocialPublicationErrorCode.NetworkError,
                                "حدث خطأ غير متوقع أثناء محاولة النشر.");
                        }
                    }
                }
            }

            if (result.IsSuccess)
            {
                publication.MarkPublished(result.ExternalPostId!, result.ExternalPostUrl, now);
                note = $"تم النشر بنجاح. ExternalPostId={result.ExternalPostId}";
            }
            else
            {
                var retryDelay = SocialPublicationRetryPolicy.ComputeDelay(
                    publication.RetryCount, _retryOptions.BaseDelay, _retryOptions.MaxDelay, _retryOptions.MaxJitterMilliseconds);
                publication.MarkFailed(result.ErrorCode!.Value, result.ErrorMessage ?? "فشل غير معروف.", now, retryDelay);
                note = publication.ErrorMessage;
            }
        }

        await _history.AddAsync(
            SocialPublicationStatusHistory.Record(
                publication.Id, fromStatus, publication.Status, changedByUserId: null, note, now),
            ct);

        await _uow.SaveChangesAsync(ct);

        return SocialDistributionMapper.ToDto(publication);
    }

    /// <summary>
    /// Generates (or reuses, via the generator's own checksum-based dedupe) the branded template
    /// asset for this publication's platform/property, and attaches it to the content. Never
    /// throws — a generation failure is classified and returned as an <see cref="AssetOutcome"/>
    /// so the caller can fail the publication exactly like a provider failure (spec §23: "لا تنشر
    /// Publication" when asset generation fails).
    /// </summary>
    private async Task<AssetOutcome> TryAttachGeneratedAssetAsync(SocialPublication publication, CancellationToken ct)
    {
        try
        {
            var asset = await _assetGenerator.GenerateAsync(
                new GenerateSocialAssetRequest(
                    publication.PropertyId,
                    publication.Content!.Platform,
                    TemplateId: "default",
                    publication.Content.Language,
                    ImageUrls: string.IsNullOrWhiteSpace(publication.Content.ImageUrl) ? [] : [publication.Content.ImageUrl],
                    Title: publication.Content.Title,
                    Body: publication.Content.Body),
                forceRegenerate: false,
                ct);

            publication.Content.AttachGeneratedAsset(asset.AssetId, asset.FileUrl);
            return AssetOutcome.NotNeeded;
        }
        catch (SocialAssetGenerationException ex)
        {
            _logger.LogWarning(
                ex,
                "Social media asset generation failed for publication {PublicationId} (retryable={Retryable}).",
                publication.Id,
                ex.Retryable);

            return AssetOutcome.Failure(
                ex.Retryable ? SocialPublicationErrorCode.TemporaryUnavailable : SocialPublicationErrorCode.UnsupportedMedia,
                ex.Message);
        }
    }

    private static SocialPublishRequest BuildPublishRequest(SocialPublication publication, SocialAccount account) => new()
    {
        PublicationId = publication.Id,
        Platform = account.Platform,
        ExternalAccountId = account.ExternalAccountId,
        CredentialReference = account.CredentialReference,
        Title = publication.Content!.Title,
        Body = publication.Content.Body,
        ImageUrl = publication.Content.ImageUrl,
        TargetUrl = publication.Content.TargetUrl,
        Hashtags = publication.Content.HashtagList,
        Language = publication.Content.Language,
    };

    private static void FailNonRetryable(SocialPublication publication, SocialPublicationErrorCode errorCode, string message, DateTime now) =>
        // TimeSpan.Zero is irrelevant here — these two error codes are never retryable
        // (SocialPublicationErrorCodeExtensions.IsRetryable), so MarkFailed always lands on
        // Failed, never Retrying, regardless of the delay passed in.
        publication.MarkFailed(errorCode, message, now, TimeSpan.Zero);

    private readonly record struct AssetOutcome(bool Failed, SocialPublicationErrorCode? ErrorCode, string? ErrorMessage)
    {
        public static readonly AssetOutcome NotNeeded = new(false, null, null);

        public static AssetOutcome Failure(SocialPublicationErrorCode errorCode, string message) => new(true, errorCode, message);
    }
}
