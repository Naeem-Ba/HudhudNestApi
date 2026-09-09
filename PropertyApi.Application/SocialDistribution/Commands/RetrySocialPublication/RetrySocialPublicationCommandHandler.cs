using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;
using PropertyApi.Domain.SocialDistribution.Entities;

namespace PropertyApi.Application.SocialDistribution.Commands.RetrySocialPublication;

public sealed class RetrySocialPublicationCommandHandler
    : IRequestHandler<RetrySocialPublicationCommand, SocialPublicationDto>
{
    private readonly ISocialPublicationRepository _publications;
    private readonly ISocialPublicationStatusHistoryRepository _history;
    private readonly IUnitOfWork _uow;

    public RetrySocialPublicationCommandHandler(
        ISocialPublicationRepository publications,
        ISocialPublicationStatusHistoryRepository history,
        IUnitOfWork uow)
    {
        _publications = publications;
        _history = history;
        _uow = uow;
    }

    public async Task<SocialPublicationDto> Handle(RetrySocialPublicationCommand request, CancellationToken ct)
    {
        var publication = await _publications.GetByIdAsync(request.PublicationId, ct)
            ?? throw new NotFoundException("منشور التوزيع غير موجود.");

        var fromStatus = publication.Status;
        var now = DateTime.UtcNow;
        publication.RetryManually(now);
        _publications.Update(publication);

        await _history.AddAsync(
            SocialPublicationStatusHistory.Record(publication.Id, fromStatus, publication.Status, request.ActorUserId, "إعادة محاولة يدوية من قِبل مسؤول.", now),
            ct);

        await _uow.SaveChangesAsync(ct);

        return SocialDistributionMapper.ToDto(publication);
    }
}
