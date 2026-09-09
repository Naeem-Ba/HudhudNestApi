using MediatR;
using PropertyApi.Application.Common.Exceptions;
using PropertyApi.Application.SocialDistribution.DTOs;
using PropertyApi.Application.SocialDistribution.Interfaces;
using PropertyApi.Application.SocialDistribution.Mapping;

namespace PropertyApi.Application.SocialDistribution.Queries.GetSocialPublicationById;

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
