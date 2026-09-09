using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.Listings.Interfaces;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;
using PropertyApi.Domain.SocialDistribution.Entities;

namespace PropertyApi.Application.SocialDistribution.Commands.QueueSocialPublication;

/// <summary>
/// Re-validates the same invariants CreateSocialPublication checked — time may have passed
/// between drafting and queueing, and the property/account may no longer be eligible (spec §13:
/// "إذا أصبح الحساب غير فعال قبل الموعد، يجب أن يفشل بشكل واضح"). Failing loudly here, before
/// anything is ever queued, is cheaper and clearer than letting the worker discover it later.
/// </summary>
public sealed class QueueSocialPublicationCommandHandler
    : IRequestHandler<QueueSocialPublicationCommand, SocialPublicationDto>
{
    private readonly ISocialPublicationRepository _publications;
    private readonly ISocialPublicationStatusHistoryRepository _history;
    private readonly ISocialAccountRepository _accounts;
    private readonly IPropertyRepository _properties;
    private readonly ISocialPublicationJobQueue _jobQueue;
    private readonly IUnitOfWork _uow;

    public QueueSocialPublicationCommandHandler(
        ISocialPublicationRepository publications,
        ISocialPublicationStatusHistoryRepository history,
        ISocialAccountRepository accounts,
        IPropertyRepository properties,
        ISocialPublicationJobQueue jobQueue,
        IUnitOfWork uow)
    {
        _publications = publications;
        _history = history;
        _accounts = accounts;
        _properties = properties;
        _jobQueue = jobQueue;
        _uow = uow;
    }

    public async Task<SocialPublicationDto> Handle(QueueSocialPublicationCommand request, CancellationToken ct)
    {
        var publication = await _publications.GetByIdAsync(request.PublicationId, ct)
            ?? throw new NotFoundException("منشور التوزيع غير موجود.");

        var propertyStillPublic = await _properties.IsPubliclyVisibleAsync(publication.PropertyId, ct);
        if (!propertyStillPublic)
            throw new ConflictException("لم يعد العقار متاحاً للعامة، لا يمكن جدولة النشر.");

        var account = await _accounts.GetByIdAsync(publication.SocialAccountId, ct);
        if (account is null || !account.CanPublish())
            throw new ConflictException("الحساب الاجتماعي المستهدف لم يعد فعّالاً.");

        var fromStatus = publication.Status;
        publication.Queue(request.ScheduledAt, DateTime.UtcNow);
        _publications.Update(publication);

        await _history.AddAsync(
            SocialPublicationStatusHistory.Record(
                publication.Id, fromStatus, publication.Status, request.ActorUserId,
                request.ScheduledAt is null ? "تم وضعه في قائمة الانتظار للنشر الفوري." : "تمت جدولة النشر.",
                DateTime.UtcNow),
            ct);

        await _uow.SaveChangesAsync(ct);

        // Phase 6: signal the Queue Port now that the row is durably Queued/Retrying in the DB —
        // see ISocialPublicationJobQueue's remarks for why this is a logging no-op today and how
        // that changes for a real external queue adapter.
        await _jobQueue.EnqueueAsync(publication.Id, ct);

        return SocialDistributionMapper.ToDto(publication);
    }
}
