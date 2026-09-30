using MediatR;
using HudhudNestApi.Application.Properties.DTOs;
using HudhudNestApi.Application.SocialDistribution.DTOs;
using HudhudNestApi.Application.SocialDistribution.Interfaces;
using HudhudNestApi.Application.SocialDistribution.Mapping;

namespace HudhudNestApi.Application.SocialDistribution.Queries.ListSocialPublications;

public sealed class ListSocialPublicationsQueryHandler
    : IRequestHandler<ListSocialPublicationsQuery, PagedResult<SocialPublicationDto>>
{
    private readonly ISocialPublicationRepository _publications;

    public ListSocialPublicationsQueryHandler(ISocialPublicationRepository publications) => _publications = publications;

    public async Task<PagedResult<SocialPublicationDto>> Handle(ListSocialPublicationsQuery request, CancellationToken ct)
    {
        var page = await _publications.GetPagedAsync(request.Filter, ct);

        return new PagedResult<SocialPublicationDto>
        {
            Items = page.Items.Select(SocialDistributionMapper.ToDto).ToList(),
            TotalCount = page.TotalCount,
            Page = page.Page,
            PageSize = page.PageSize,
        };
    }
}
