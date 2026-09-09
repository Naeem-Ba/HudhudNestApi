using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;
using PropertyApi.Domain.SocialDistribution.Entities;

namespace PropertyApi.Application.SocialDistribution.Commands.CancelSocialPublication;

public sealed class CancelSocialPublicationCommandHandler
    : IRequestHandler<CancelSocialPublicationCommand, SocialPublicationDto>
{
    private readonly ISocialPublicationRepository _publications;
    private readonly ISocialPublicationStatusHistoryRepository _history;
    private readonly IUnitOfWork _uow;

    public CancelSocialPublicationCommandHandler(
        ISocialPublicationRepository publications,
        ISocialPublicationStatusHistoryRepository history,
        IUnitOfWork uow)
    {
        _publications = publications;
        _history = history;
        _uow = uow;
    }

    public async Task<SocialPublicationDto> Handle(CancelSocialPublicationCommand request, CancellationToken ct)
    {
        var publication = await _publications.GetByIdAsync(request.PublicationId, ct)
            ?? throw new NotFoundException("منشور التوزيع غير موجود.");

        var fromStatus = publication.Status;
        var now = DateTime.UtcNow;
        publication.Cancel(now);
        _publications.Update(publication);

        await _history.AddAsync(
            SocialPublicationStatusHistory.Record(publication.Id, fromStatus, publication.Status, request.ActorUserId, "تم الإلغاء يدوياً.", now),
            ct);

        await _uow.SaveChangesAsync(ct);

        return SocialDistributionMapper.ToDto(publication);
    }
}
