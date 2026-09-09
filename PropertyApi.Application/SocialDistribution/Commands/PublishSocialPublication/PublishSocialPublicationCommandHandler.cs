using MediatR;
using Microsoft.Extensions.Logging;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;
using PropertyApi.Domain.SocialDistribution.Entities;
using PropertyApi.Domain.SocialDistribution.Enums;
using PropertyApi.Domain.SocialDistribution.Models;
using PropertyApi.Domain.SocialDistribution.Policies;

namespace PropertyApi.Application.SocialDistribution.Commands.PublishSocialPublication;

/// <summary>
/// The heart of the distribution engine. Re-validates the property/account are still eligible
/// AT THE MOMENT OF EXECUTION (not just when queued — spec §13: an account/property can become
/// ineligible while a publication is waiting), then delegates the actual platform call to
/// whichever <see cref="ISocialPublisher"/> matches the account's platform.
///
/// Concurrency: <see cref="SocialPublication.StartPublishing"/> only accepts Queued/Retrying,
/// so a second concurrent call against the same row either loses the race on SaveChanges
/// (xmin optimistic-concurrency token — see SocialPublicationConfiguration) or, if it reads
/// after the first already committed, fails EnsureStatus immediately. The dispatch worker
/// additionally never selects the same row twice in the same sweep (see
/// ISocialPublicationRepository.GetDueToPublishAsync/GetDueForRetryAsync + its Postgres advisory
/// lock), so double-execution across separate worker instances is the one scenario a real
/// distributed lease/lock would still need to close out further — documented as a limitation.
/// </summary>
public sealed class PublishSocialPublicationCommandHandler
    : IRequestHandler<PublishSocialPublicationCommand, SocialPublicationDto>
{
    private readonly ISocialPublicationRepository _publications;
    private readonly ISocialPublicationStatusHistoryRepository _history;
    private readonly ISocialAccountRepository _accounts;
    private readonly IPropertyRepository _properties;
    private readonly IEnumerable<ISocialPublisher> _publishers;
    private readonly IUnitOfWork _uow;
    private readonly ILogger<PublishSocialPublicationCommandHandler> _logger;

    public PublishSocialPublicationCommandHandler(
        ISocialPublicationRepository publications,
        ISocialPublicationStatusHistoryRepository history,
        ISocialAccountRepository accounts,
        IPropertyRepository properties,
        IEnumerable<ISocialPublisher> publishers,
        IUnitOfWork uow,
        ILogger<PublishSocialPublicationCommandHandler> logger)
    {
        _publications = publications;
        _history = history;
        _accounts = accounts;
        _properties = properties;
        _publishers = publishers;
        _uow = uow;
        _logger = logger;
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
            var publisher = _publishers.FirstOrDefault(p => p.Platform == account.Platform);

            SocialPublishResult result;
            if (publisher is null)
            {
                result = SocialPublishResult.Failure(
                    SocialPublicationErrorCode.PlatformNotConfigured,
                    "لا يوجد Publisher مسجل لهذه المنصة.");
            }
            else
            {
                var publishRequest = new SocialPublishRequest
                {
                    PublicationId = publication.Id,
                    Platform = account.Platform,
                    ExternalAccountId = account.ExternalAccountId,
                    CredentialReference = account.CredentialReference,
                    Title = publication.Content.Title,
                    Body = publication.Content.Body,
                    ImageUrl = publication.Content.ImageUrl,
                    TargetUrl = publication.Content.TargetUrl,
                    Hashtags = publication.Content.HashtagList,
                    Language = publication.Content.Language,
                };

                try
                {
                    result = await publisher.PublishAsync(publishRequest, ct);
                }
                catch (Exception ex)
                {
                    // A publisher must not throw for an ordinary platform failure (see
                    // ISocialPublisher remarks) — an exception escaping it is unexpected, so it
                    // is logged in full here but the publication itself only ever stores the
                    // sanitized, generic message below, never the raw exception (spec §22).
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

            if (result.IsSuccess)
            {
                publication.MarkPublished(result.ExternalPostId!, result.ExternalPostUrl, now);
                note = $"تم النشر بنجاح. ExternalPostId={result.ExternalPostId}";
            }
            else
            {
                var retryDelay = SocialPublicationRetryPolicy.ComputeDelay(publication.RetryCount);
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

    private static void FailNonRetryable(SocialPublication publication, SocialPublicationErrorCode errorCode, string message, DateTime now) =>
        // TimeSpan.Zero is irrelevant here — these two error codes are never retryable
        // (SocialPublicationErrorCodeExtensions.IsRetryable), so MarkFailed always lands on
        // Failed, never Retrying, regardless of the delay passed in.
        publication.MarkFailed(errorCode, message, now, TimeSpan.Zero);
}
