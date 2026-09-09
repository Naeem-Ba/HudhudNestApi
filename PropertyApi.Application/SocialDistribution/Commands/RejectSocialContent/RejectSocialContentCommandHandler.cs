using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.Common.Interfaces;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;

namespace PropertyApi.Application.SocialDistribution.Commands.RejectSocialContent;

public sealed class RejectSocialContentCommandHandler : IRequestHandler<RejectSocialContentCommand, SocialPublicationDto>
{
    private readonly ISocialPublicationRepository _publications;
    private readonly IUnitOfWork _uow;

    public RejectSocialContentCommandHandler(ISocialPublicationRepository publications, IUnitOfWork uow)
    {
        _publications = publications;
        _uow = uow;
    }

    public async Task<SocialPublicationDto> Handle(RejectSocialContentCommand request, CancellationToken ct)
    {
        var publication = await _publications.GetByIdAsync(request.PublicationId, ct)
            ?? throw new NotFoundException("منشور التوزيع غير موجود.");

        if (publication.Content is null)
            throw new ConflictException("لا يوجد محتوى لهذا المنشور.");

        publication.Content.Reject(request.ActorUserId, request.Note, DateTime.UtcNow);
        _publications.Update(publication);
        await _uow.SaveChangesAsync(ct);

        return SocialDistributionMapper.ToDto(publication);
    }
}
