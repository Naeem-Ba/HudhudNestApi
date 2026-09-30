using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;
using HudhudNestApi.Domain.SocialDistribution.Entities;

namespace HudhudNestApi.Application.SocialDistribution.Commands.RescheduleSocialPublication;

public sealed class RescheduleSocialPublicationCommandHandler : IRequestHandler<RescheduleSocialPublicationCommand, SocialPublicationDto>
{
    private readonly ISocialPublicationRepository _publications;
    private readonly ISocialPublicationStatusHistoryRepository _history;
    private readonly IUnitOfWork _uow;

    public RescheduleSocialPublicationCommandHandler(
        ISocialPublicationRepository publications, ISocialPublicationStatusHistoryRepository history, IUnitOfWork uow)
    {
        _publications = publications;
        _history = history;
        _uow = uow;
    }

    public async Task<SocialPublicationDto> Handle(RescheduleSocialPublicationCommand request, CancellationToken ct)
    {
        var publication = await _publications.GetByIdAsync(request.PublicationId, ct)
            ?? throw new NotFoundException("منشور التوزيع غير موجود.");

        var now = DateTime.UtcNow;
        publication.Reschedule(request.NewScheduledAt, now);
        _publications.Update(publication);

        await _history.AddAsync(
            SocialPublicationStatusHistory.Record(
                publication.Id, publication.Status, publication.Status, request.ActorUserId,
                request.NewScheduledAt is null
                    ? "تمت إعادة الجدولة للنشر في أقرب فرصة."
                    : $"تمت إعادة الجدولة إلى {request.NewScheduledAt:O}.",
                now),
            ct);

        await _uow.SaveChangesAsync(ct);

        return SocialDistributionMapper.ToDto(publication);
    }
}
