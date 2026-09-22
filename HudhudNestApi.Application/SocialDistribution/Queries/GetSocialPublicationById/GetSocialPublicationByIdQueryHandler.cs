using MediatR;
using HudhudNestApi.Application.Common.Exceptions;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;

namespace HudhudNestApi.Application.SocialDistribution.Queries.GetSocialPublicationById;

public sealed class GetSocialPublicationByIdQueryHandler : IRequestHandler<GetSocialPublicationByIdQuery, SocialPublicationDto>
{
    private readonly ISocialPublicationRepository _publications;

    public GetSocialPublicationByIdQueryHandler(ISocialPublicationRepository publications) => _publications = publications;

    public async Task<SocialPublicationDto> Handle(GetSocialPublicationByIdQuery request, CancellationToken ct)
    {
        var publication = await _publications.GetByIdAsync(request.PublicationId, ct)
            ?? throw new NotFoundException("منشور التوزيع غير موجود.");

        return SocialDistributionMapper.ToDto(publication);
    }
}
