using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.Common.Interfaces;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;

namespace HudhudNestApi.Application.SocialDistribution.Commands.ApproveSocialContent;

public sealed class ApproveSocialContentCommandHandler : IRequestHandler<ApproveSocialContentCommand, SocialPublicationDto>
{
    private readonly ISocialPublicationRepository _publications;
    private readonly IUnitOfWork _uow;

    public ApproveSocialContentCommandHandler(ISocialPublicationRepository publications, IUnitOfWork uow)
    {
        _publications = publications;
        _uow = uow;
    }

    public async Task<SocialPublicationDto> Handle(ApproveSocialContentCommand request, CancellationToken ct)
    {
        var publication = await _publications.GetByIdAsync(request.PublicationId, ct)
            ?? throw new NotFoundException("منشور التوزيع غير موجود.");

        if (publication.Content is null)
            throw new ConflictException("لا يوجد محتوى لهذا المنشور.");

        publication.Content.Approve(request.ActorUserId, DateTime.UtcNow, request.Note);
        _publications.Update(publication);
        await _uow.SaveChangesAsync(ct);

        return SocialDistributionMapper.ToDto(publication);
    }
}
